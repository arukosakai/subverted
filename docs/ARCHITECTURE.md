# Architecture

## Layout

```
src/Subverted.Core      Domain model. No dependencies, no I/O.
src/Subverted.Svn       SVN adapter: wc.db reads, status rules, CLI fallback.
src/Subverted.Protocol  IPC contracts and client.
src/Subverted.Daemon    Resident host: index, watcher, IPC server.
src/Subverted.Frontend  What every front-end shares: reaching the daemon, starting it if need be.
src/Subverted.Cli       `sv`.
src/Subverted.App       Avalonia front-end.                          (M2)
tools/Subverted.Probe   Throwaway timing and correctness harness.
tests/                  One test project per src project.
```

## Dependency rules

These are the load-bearing constraint. Everything else is style.

```
Core     ← Svn ← Daemon
  ↑              ↑
Protocol ────────┘
  ↑
Frontend
  ↑
Cli, App
```

- **Core depends on nothing.** If it needs a package reference, the design is wrong.
- **Svn is the only project that knows SVN exists.** No other project may reference
  `Microsoft.Data.Sqlite`, shell out to `svn`, or know what `op_depth` means.
- **Cli and App depend on Frontend only**, and through it on Protocol. They never reference
  `Subverted.Svn` directly. If a front-end needs something, it goes through the daemon — otherwise we end up with two divergent
  implementations of status, which is exactly how TortoiseSVN got slow.
- **Protocol carries data, not behaviour.** Records and interfaces, no logic — beyond the client
  that moves them. Logic both sides of the wire need is Core's if it is pure (`TargetCoverage`),
  and Frontend's if only front-ends need it (`DaemonChannel`).

## Status request flow

```
sv st
  → DaemonClient, Unix domain socket  (starting the daemon if nothing answers)
    → WorkingCopySessions: which working copy contains this path?
      → WorkingCopySession: has the generation moved since the last scan?
        → no:  answer from memory
        → yes: WorkingCopyScanner → WcDbReader → .svn/wc.db
               → on WcDbException(Unreadable): SvnInfoCommand + SvnStatusCommand  (D14)
    → StatusFilter drops what the request did not ask for
  → StatusReport renders, StatusPalette colours, ConsolePager pages
```

The daemon answers from memory in the common case. Filtering happens daemon-side so that a clean
hundred-thousand-file checkout answers with an empty list rather than serialising a hundred
thousand nodes nobody asked about. The one request that does ask for all of them — the GUI's All listing —
names the scan it already holds, and is told "unchanged" instead of sent them again (D35).

`sv log` and `sv d` take the same route as far as the session and then leave it:

```
sv log / sv d
  → DaemonClient, Unix domain socket
    → WorkingCopySessions: which working copy contains this path?
      → SvnLogCommand / SvnDiffCommand: `svn` in that root, path relative to it  (D6)
        → SvnLogXml → RevisionEntry     |  diff text, unparsed
  → LogReport / DiffReport render, LogPalette / DiffPalette colour, ConsolePager pages
```

The session is used for the root, not for the index: history and diff are never served from
memory. Nothing warm can answer "what is on the server", and a diff read from an index that is a
generation stale is worse than a slow one.

**The CLI fallback is built (D14).** `WcDbException` carries a `WcDbFailure` — `NotAWorkingCopy` is
the caller's path being wrong, `Unreadable` is the fast path failing on a working copy that is
really there. The first still fails the request, because asking the client about a path with no
working copy only makes a clear answer slower and vaguer. The second is answered by
`WorkingCopySessionFactory` opening the session over `svn status --xml` instead, which is what
principle 3 asks for: a format bump is no longer the day `sv st` stops answering.

## Decisions

### D1 — Read wc.db directly, with a CLI fallback

Since 1.7, SVN keeps working-copy metadata in a single SQLite file. Reading it skips process
startup and SVN's own tree walk, which is where `svn status` spends its time.

The schema is private to Subversion and carries no compatibility guarantee. So: the reader
version-gates on `PRAGMA user_version` (29–31), opens read-only, and throws `WcDbException` on
anything unexpected. Callers treat that as "use the CLI", never as fatal.

Two things that measurement contradicted, recorded so nobody re-tries them:

- Rewriting the highest-`op_depth` projection with a window function is **8× slower** than the
  correlated subquery (542 ms vs 68 ms on 20k nodes). The subquery rides the primary key index;
  the window version forces a full partition sort. Leave it alone.
- Reading wc.db is not in itself faster than `svn status`. The query is 68 ms of a 326 ms scan;
  the rest is filesystem metadata, which is already at the floor for this machine. Where we do win
  outright is the case where content has to be compared — 4.3× on a 20k tree. See PLAN.md for both
  measurements and the caveats on the second.

*Status: implemented and verified against SVN 1.8.15 (format 31). Read-only open works because
wc.db uses `journal_mode = delete`, not WAL.*

### D2 — Modification is three-state

`Unmodified` / `NeedsPristineCompare` / `Modified`.

Size and mtime matching SVN's recorded values *prove* a file is clean. A mismatch proves nothing —
saving a file without editing it moves the mtime. So the fast path can only rule things out, and
the negative case escalates to a content compare.

Collapsing this to a bool produces a status list that reports hundreds of untouched assets as
modified, which trains people to ignore it.

`NeedsPristineCompare` is a stage, not usually a final answer — `PristineComparer` (D7) settles
it. It survives to the caller only when the answer is genuinely unknowable.

*Status: implemented, 100% branch coverage.*

### D7 — Content compare by SHA-1, not by reading the pristine

Nodes the fast path cannot prove clean are hashed and compared against `NODES.checksum`
(`$sha1$<40 lowercase hex>`). Hashing the working file is simpler than locating and streaming the
pristine, and needs no pristine file to be present.

A node whose working file is a *translation* of its pristine cannot be settled this way, and falls
back to undecided rather than being called modified — see D8 for which properties mean that.

*Status: implemented and verified — a same-size rewrite that `svn status` calls clean now
resolves to Unmodified. Reopened by D15 and closed again by D18: the ~1.6× the pristine compare
measured on multi-megabyte assets was mostly the OS file cache, and cold the two overlap. The
secondary reason given above —
"needs no pristine file to be present" — has since become the stronger one: SVN 1.15 makes
pristines optional (`--store-pristine=no`), and `svn` itself now falls back to a checksum in
exactly that case. So this stays as the path that always works, and a pristine compare, if it is
taken, is an optimisation on top of it rather than a replacement for it.*

*Decided 2026-09-23: it is not taken. Hashing is the only content path. The one edge left — ~1.3× on
a machine whose cores are busy — does not pay for a second path on the code that decides whether
someone's work is committed.*

### D8 — Parse the property skel rather than treating any property as opaque

The original reader could not read SVN's property format, so it treated *any* property as a reason
to give up on the checksum compare. That is conservative in the safe direction but far too broad:
`svn:needs-lock` is on essentially every file in a game asset tree and changes nothing on disk, so
every locked asset whose mtime had moved came back "I don't know" — an answer a real client can
only resolve by shelling out to `svn status`, which is the cost the fast path existed to avoid.

`SvnPropertySkel` parses the format instead. It is a flat list of alternating name and value
atoms; an atom is written bare when it starts with a letter and contains no whitespace or
parenthesis, and as `<length><one space><bytes>` otherwise. The single space is what lets a value
begin with a digit, a space or a parenthesis unambiguously. A blob that does not parse returns
null, and the caller treats that exactly as it treats a translating property — undecided.

Only `svn:eol-style`, `svn:keywords` and `svn:special` change the bytes on disk. Everything else,
including `svn:needs-lock`, `svn:mime-type` and `svn:executable`, now keeps its fast path.

Two things worth knowing before touching this:

- **Working properties are not `NODES.properties`.** That column is the *pristine* set.
  `ACTUAL_NODE.properties` overrides it whenever that row exists with a non-NULL column, which is
  what `workingProperties ?? pristineProperties` in the reader implements. Reading `NODES` alone
  made a locally deleted `svn:needs-lock` still look set, and — the dangerous direction — a
  locally added `svn:eol-style` look absent. The reader selects both columns rather than
  `COALESCE`-ing them in SQL, because D9 needs the pair, not the winner.
- **The format was verified, not inferred.** The test blobs were copied byte-for-byte out of a
  working copy built to carry an empty value, a multi-line `svn:ignore`, and a value containing
  spaces and parentheses. Hand-written skels are variations on those; if the two ever disagree,
  the real format wins.

Measured on a 20k-node copy with `svn:needs-lock` committed on every node and every mtime moved —
the state a working copy is in after any real session of work. All 20,200 nodes now resolve to
Unmodified in a 2.15 s scan. Before this, all 20,200 returned `NeedsPristineCompare` in ~330 ms:
faster, and worth nothing, because the only way to resolve that answer is `svn status`, which
takes 9.6 s on the same tree. Being able to answer at all is what turned this case from a
fallback into a 4.3× win.

*Status: implemented and verified against SVN 1.8.15 (format 31). Pure and I/O-free, 100%
line and branch coverage.*

### D9 — Property status is a second axis, not another `NodeStatus` member

`svn status` prints two columns, and they are independent: a node can be clean on content and
dirty on properties (` M`), dirty on both (`MM`), or missing from disk *and* property-modified
(`!M` — a real node in the 20k fixture). A one-dimensional status cannot say that. We reported
such nodes as `Unmodified`: a clean-looking answer for a node the user still has to commit.

`WorkingCopyEntry` now carries `NodeStatus Status` and `PropertyStatus PropertyStatus`.
Folding property changes into `NodeStatus.Modified` was rejected because it destroys the
distinction a diff view needs, and an eleventh `NodeStatus` member was rejected because it cannot
represent `MM`. Decision taken 2026-09-19.

`PropertyStatus` has two members and deliberately no `NeedsCompare`: both property sets live in
wc.db, so unlike the content axis the comparison is exact and never escalates to the filesystem.

What the fixture settled, none of it inferred:

- **`op_depth > 0` blanks the column.** A copy whose properties differ from its source prints
  `A  +`, not `AM` — confirmed by `svn diff` showing the property change that status omits. The
  add is what carries the properties, so op_depth decides before the sets are compared.
- **NULL and `()` are different claims.** A NULL `ACTUAL_NODE.properties` means "no local change";
  an empty skel means "this node has no properties" and against a non-empty pristine set it is a
  property *deletion*. SVN also prunes the blob back to NULL — leaving the row behind — when a
  property is set to the value it already had, so row presence is not the signal.
- **Order is not compared.** SVN writes these alphabetically but the format does not promise it,
  so the compare is by name and value.

An unparseable skel reports `Modified`. That is the opposite lean from D8, on purpose: there,
guessing wrong costs a slow path; here, guessing "clean" hides work from a commit.

Two divergences from `svn status` remain, both over-reporting rather than hiding:

- A **property conflict** prints ` C` — text column blank — but `ACTUAL_NODE.conflict_data` is a
  skel we do not parse, so we cannot tell a property conflict from a text one and report the whole
  node `Conflicted`. Louder than SVN, never quieter.
- **Unversioned files** are still not detected, so the `.prej` reject file a property conflict
  leaves behind does not appear.

*Status: implemented and verified against SVN 1.8.15 (format 31) on a fixture carrying
` M`, `MM`, `M `, `!M`, ` C`, `A ` and `A  +` nodes. Pure and I/O-free. No measurable cost: the
20k-node scan is unchanged at ~2.3 s, because a whole working copy typically has a handful of
`ACTUAL_NODE` rows and nothing else is parsed.*

### D10 — Reproduce SVN's ignore rules rather than approximate them

Unversioned detection is cheap — the tree walk already indexes every path, so anything on disk
that wc.db has no row for is unversioned. Ignoring is the hard half, and getting it wrong is not
cosmetic: an artist whose engine spits out a build tree wants to see the six files they changed,
not six thousand they did not.

Every rule below was read off `svn status` on a fixture built for it, because the documentation is
wrong about one of them:

- **`svn:ignore` reaches immediate children only.** A root `*.tmp` leaves `sub/child.tmp`
  reported. **`svn:global-ignores` reaches every descendant.**
- **Both properties are newline-delimited.** The docs describe `svn:global-ignores` as
  whitespace-delimited like its config-file namesake. On 1.8.15 it is not: a space-separated value
  ignored nothing, and a space-separated `svn:ignore` matched only a file with a space in its
  name. Only the runtime config's `global-ignores` splits on whitespace.
- **The runtime config replaces the built-in list, it does not extend it.** Confirmed by pointing
  `svn` at a throwaway `--config-dir`: the configured patterns took effect and `*.o` stopped
  being ignored.
- **Globs are case-sensitive, on Windows too.** `CASE.TMP` sat unignored beside an ignored
  `root.tmp`. And `*` matches a leading dot, so `*.rej` ignores `.foo.rej` — SVN's own config file
  says so, and plain fnmatch disagrees.
- **An unversioned directory is reported once and never descended into**, ignored or not. So is an
  ignored one.
- **The built-in list does not contain `Thumbs.db`**, which is worth stating because everyone
  assumes it does. Each of the seventeen patterns was confirmed a file at a time.

`SvnGlob` implements `*`, `?` and `[...]` with ranges and negation; `WorkingCopyIgnoreRules`
accumulates each directory's inherited patterns once at build time so a lookup never walks back up
the tree. The working-copy root is now part of the node projection too — `svn status` reports it
as `.` when its own properties change, and we were silently dropping it.

Not implemented: Subversion's Windows registry configuration override. A machine configured that
way will disagree with `svn status`, and nothing warns about it.

*Status: implemented and verified. The probe now diffs clean against `svn status --no-ignore`,
line for line, on a fixture carrying thirty-nine unversioned and ignored entries.*

### D11 — An invalidated size cache is not a size

Setting a property that affects translation makes SVN write `translated_size = -1` and
`last_mod_time = 0` — sentinels, not NULL. We read `-1` as a real recorded size, compared it
against the file's actual length, and reported an untouched file as **Modified**. `svn status`
calls it text-clean.

`WcDbRow.RecordedSize` now reports null for a negative stored size, which sends the node to the
content compare where it belongs. The companion `last_mod_time = 0` deliberately gets no sentinel
of its own: an unrecorded size already forces the compare, and 0 is a legitimate APR time that an
existing boundary test pins.

Only *translating* properties do this — `svn:mime-type` and `svn:needs-lock` leave the cache
intact, which is why the bug hid behind every property fixture built before one used
`svn:eol-style`.

Worth recording for its own sake: this was found by the first integration test written against a
real working copy, not by review, and not by any of the unit tests — all of which were green and
none of which could have caught it, because they fed the resolver a row shape that only a real
propset produces.

*Status: fixed, with a unit test on the sentinel and an integration test that reproduces the
original report across all three property kinds.*

### D12 — Once the daemon is warm, the front-end *is* the cost

Measured on the 20,203-node fixture with the daemon already holding it, Release build:

| | time |
|---|---|
| daemon, warm answer | **1.4 ms** |
| `sv` process, everything else | ~320 ms in-process, ~475 ms wall |

The daemon side of M1's 50 ms criterion is met by a factor of thirty-five. The number a user
actually experiences is ten times *over* it, and essentially none of that is Subverted's code —
it is the .NET runtime starting, twice a second, for a program whose work takes a millisecond.

What this rules out: micro-optimising the scan, the filter, the wire format or the socket. Their
combined contribution is under two milliseconds and there is nothing there to win.

What it points at: compiling the front-end ahead of time. `PublishReadyToRun` is on and buys
little (~475 ms against ~500 ms) because the cost is runtime startup, not JIT. NativeAOT is the
lever that matters, and the protocol was built reflection-free (`ProtocolJsonContext`,
source-generated) specifically so that it stays available. `-p:PublishAot=true` compiles the whole
front-end with **zero trim or AOT warnings** — the IL is clean. It could not be linked on the
development machine, which has no MSVC linker, so **the resulting binary has never been run or
timed.** That is the single highest-value unproven claim in the project right now.

The daemon stays JIT-compiled on purpose. It starts once and lives for days; its startup cost is
paid by nobody.

*Status: measured. The conclusion is acted on as far as it can be without a linker.*

### D13 — When content has to be compared, the compare *is* the scan

At 100k nodes in the state real work leaves behind — every recorded mtime moved, every byte
unchanged — a scan took 14.4–17.0 s, and all but about a second of that was
`PristineComparer.Compare` opening one file at a time on one thread. The metadata fast path
settles nothing in that state, so every node is hashed (D7, D8).

Nothing in that loop is shared: a node's verdict depends on its own row and its own bytes, and
`PristineComparer` holds no state. It was sequential only because it began life as one loop inside
the tree walk. `WorkingCopyScanner` now sets those nodes aside as it walks and hashes the batch
with `Parallel.For`.

Measured A/B on the 101,001-node fixture, same binary, same cache, back to back, three runs each:

| | scan |
|---|---|
| one thread | 17.0 / 15.0 / 14.4 s |
| all 8 cores | 3.8 / 3.5 / 4.2 s |

**~4.1×, not 8×** — at 29 bytes a file the work is one `open` syscall, not arithmetic, so it stops
scaling well before the core count. Worth knowing before anyone tries to buy the rest of it back
with more threads. For reference `svn status` on the same tree in the same state takes ~40 s.

That explanation is true of *this* fixture and does not generalise: D15 measures the same loop on
multi-megabyte assets, where the bytes dominate the syscall and it scales ~7.8× instead.

Two things this deliberately does not do:

- **It costs nothing when nothing needs hashing.** The undecided nodes are collected during the
  walk, and a fresh checkout collects none, so the parallel pass is skipped entirely rather than
  paying `Parallel.For` setup on 100k items to do no work.
- **It does not make the scan incremental.** A change anywhere still rescans the whole working
  copy; this only makes that rescan four times cheaper in the state where it hurts. The real fix
  is still re-resolving only what changed — see PLAN.md.

Verdicts are written into their own array before being folded back into the entry list, because
`List<T>` promises nothing about concurrent writes even to distinct indices. The test that pins
this alternates clean and modified files of identical length, so a verdict delivered to the wrong
node shows as a wrong status rather than as a tally that still adds up.

*Status: implemented and measured. `WorkingCopyScanner` and `PristineComparer` both at 100% line
and branch; status output still diffs clean against `svn status --no-ignore` on the correctness
fixtures, with only the two divergences M0 already documented.*

### D14 — The fallback is a second reader, not a second status

Principle 3 says every direct read of wc.db has a documented route through the supported CLI. D1
wrote that route down and left it unbuilt, so a working copy this build cannot parse — the studio
upgrading Subversion, which is *when*, not *if* — was a working copy `sv st` refused while `svn
status` in the same folder kept working.

The obvious shape is the wrong one. A front-end that falls back has two implementations of status
in it, which is how TortoiseSVN got slow, and the daemon above would have to know which one
answered. So the fallback is not a second path through the daemon: it is a second
`IWorkingCopyScan`. `WorkingCopySessionFactory` picks it once, at open, and everything above —
session, watcher, generation counter, filter, renderer — cannot tell the difference. The price is
paid where it belongs: a process per scan instead of a SQLite read, and no warm-index benefit lost,
because the session still caches the answer and still rescans only when the watcher says so.

`WcDbFailure` is what makes the decision small enough to be right. `NotAWorkingCopy` is not a fast
path failing, it is the caller's path being wrong, and it fails as before. Only `Unreadable` falls
back.

Five things running it settled, none of which the documentation says:

- **`svn status` outside a working copy exits zero** with a warning and an empty document — which
  reads as "everything is clean". `svn info` is asked first and is what refuses, so an empty listing
  can never be the answer to "there is nothing here".
- **`svn status` reports no node kind at all.** `NodeKind.Unknown` is the honest answer and nothing
  renders kind today. Inventing one from the path would be a guess in a reader whose whole job is to
  agree with the fast path.
- **SVN contradicts itself on the property axis.** A copy whose properties differ from its source
  comes back `props="modified"` in the XML while `svn status` prints nothing in its second column.
  The wc.db path reaches "nothing" from `op_depth > 0`; the fallback follows the column, not the
  attribute. This was caught by running `sv st` both ways, not by a test — see PLAN.md.
- **A corrupt wc.db defeats both readers.** The fallback answers for a database this build refuses
  to read, not for one Subversion cannot read either, and it fails rather than returning nothing.
- **The second reader found a real bug in the first.** Diffing the two exposed the fast path
  reading its revision off the highest `op_depth` row, where the column means the copyfrom revision
  for a copy and nothing at all for a delete — so it printed a copy's source where `svn` prints
  `-`, and nothing for a delete and a replace where `svn` prints the BASE revision. The fallback
  was right and wc.db was wrong, which is the opposite of the direction this was built to guard.
  Now read from the op_depth 0 row, and the agreement test covers the revision column it originally
  had to leave out.

Unmodelled states fail the whole read rather than being guessed at — the rule `SvnLogXml` already
follows for an action it cannot map. `obstructed` and `external` were the two that reached it, which
meant a working copy containing an external could not use the fallback at all; D16 models both, so
what is left is `merged` and whatever SVN adds next.

*Status: implemented. `SvnStatusXml` and `SvnInfoXml` at 100% line, and 100% branch bar the hash
buckets the compiler emits for a string switch. Verified against a real repository node for node
against the wc.db path, and run end-to-end through `sv st` by narrowing the format gate so a real
working copy took the fallback: identical output on two fixtures.*

### D15 — At asset sizes the compare is arithmetic, not syscalls

Every performance number before this one was taken on files averaging 29–43 bytes. That measures
per-file overhead, which is a real cost but not the one a game studio pays: its tree is tens of
thousands of multi-megabyte binaries. `tools/make-asset-fixture.ps1` builds the missing case —
1,000 files between 256 KB and 4 MB, 1.59 GiB, random bytes, because real assets are already
compressed and compressible filler would make both the pristine store and SVN's delta encoding
unrealistically cheap to read back.

Three runs each, Release, 8 cores / 16 threads, SVN 1.8.15.

| | State A, mtimes match | State B, every mtime moved |
|---|---|---|
| `svn status` | 87 – 111 ms | 4.7 – 5.4 s |
| scan only | 174 – 204 ms | 0.86 – 0.97 s |
| `sv st` cold, daemon side | 0.14 – 0.19 s | 0.86 – 0.93 s |
| `sv st` warm, daemon side | 0.1 – 0.2 ms | 0.1 – 0.2 ms |

**PLAN.md had recorded two predictions about this case. Both hold, and they pull opposite ways.**

The ratio against `svn status` *does* compress: ~10× at 100k tiny files, **~5.4× here**. And the
parallel compare *does* hold up better rather than worse — but for the reason opposite to the one
D13 gives. D13 found ~4.1× on 8 cores and explained it as "one `open` syscall per file, not
arithmetic". **That is a statement about small files only.** At asset sizes the same loop is bound
on the arithmetic, and scales ~7.8×.

Measured rather than reasoned about, on the same tree:

| | throughput |
|---|---|
| read only, parallel, no hashing | 6.6 – 6.9 GB/s |
| read + SHA-1, parallel (what we do) | 1.95 – 2.22 GB/s |
| read + SHA-1, one thread | 0.27 – 0.28 GB/s |
| SHA-256 on the same bytes, for reference | ~1.2 GB/s |

Reading is 3× cheaper than hashing, so this is not I/O. SHA-256 beating SHA-1 by 2× is the tell:
this CPU accelerates SHA-256 and .NET's SHA-1 path is not accelerated. We cannot swap the
algorithm — wc.db records a SHA-1 — so the ceiling is what it is.

**The uncomfortable corollary, stated because it is the honest reading:** per core we are *slower*
than `svn`. Single-threaded, hashing this tree takes ~6.2 s against `svn status`'s ~5.0 s — so
whatever `svn` spends per byte is cheaper than a SHA-1. The entire advantage at asset sizes is
that we use eight cores and `svn` uses one.

**What it does is no longer inferred.** `compare_and_verify` in `libsvn_wc/questions.c` calls
`svn_stream_contents_same2` on the working file and the pristine stream. On the 1.8.x branch — the
one every number here was measured against — that function contains no checksum path whatsoever;
the recorded checksum is used only as the key that locates the pristine. There is also a size
shortcut, `!need_translation && versioned_file_size != pristine_size`, which State B defeats by
construction because the bytes are identical. So `svn` reads both files and compares them, and it
wins per byte because a stream `memcmp` beats a SHA-1 that this CPU does not accelerate.

That puts **D7 back on the table**, and it is a decision rather than a refactor — see PLAN.md.
Byte-comparing against the pristine, same tree and same parallelism, measured **454 – 518 ms
against SHA-1's 731 – 822 ms — ~1.6× faster despite reading twice the bytes**, and agreeing on
every node. Of the three things that argued against implementing it, reading Subversion's source
settled two and they point opposite ways:

- **Compressed pristines: false, and it was the easy blocker to remove.** Pristines are stored as
  raw bytes. `svn_wc__db_pristine_read` opens the file with `svn_io_file_open` and wraps it
  directly, with no decompression layer — on trunk as on 1.8. Compressed pristines is a 2012
  dev-list proposal that never landed. A `memcmp` stays a `memcmp`.
- **A missing pristine stopped being hypothetical.** 1.15 adds `svn checkout --store-pristine=no`,
  which fetches and discards pristines per file instead of keeping them all. That is exactly the
  case D7 chose hashing to survive, and it is now a supported configuration rather than a
  corruption scenario. Subversion itself hit the same wall: trunk's `compare_and_verify` grew a
  `svn_stream_contents_checksum` fallback for when pristine contents are unavailable — which is
  our primary path, arrived at from the other direction. Nothing is broken today: `WcDbReader`
  gates on formats 29–31, so a 1.15 working copy takes the D14 fallback and never reaches this
  code. It matters for what gets built next, not for what runs now.
- **The file cache still decides the rest.** The second read is free only while the tree is
  resident, and this 1.59 GiB one fits in 61.6 GB of RAM where a studio's will not. **Since
  settled, by evicting the cache rather than by growing the tree — see D18. Cold, the advantage
  disappears.**

So the shape of the answer has changed: not "hash or compare" but "compare when the pristine is
there, hash when it is not" — which is what `svn` now does, and which costs both paths rather than
replacing one with the other. Still a decision, and still waiting on the disk-cold number.

**What this measurement does not establish.** The tree fits in RAM, so every figure above is
cached throughput. Disk-cold throughput at asset sizes is the number that decides the
pristine-compare question, because that is where reading twice stops being free — **it has since
been taken, by evicting the cache, and it answers the question the other way. D18.**

*Status: measured, nothing changed in the code. The fixture and the generator are kept so the
numbers can be reproduced rather than trusted. The claims about what `svn` does internally were
read out of Subversion's source at 1.8.x and trunk — read, not run, which is a weaker kind of
evidence than the timings above and is labelled as such.*

### D16 — Obstructed and external are states, not omissions

"Not modelled" made these sound like a gap. They were two wrong answers, and both pointed the
dangerous way:

- **Obstructed** — versioned as one kind, present on disk as the other. A directory standing where
  a versioned file belongs fell through to "a directory has no content to compare" and reported
  **clean**. The reverse reached the content compare, found a directory row with no recorded size,
  and reported `NeedsPristineCompare`, which rendered as `~` and so looked right for entirely the
  wrong reason.
- **External** — an `svn:externals` checkout has no NODES row at all. It lives in the `EXTERNALS`
  table and has its own `.svn`, so the tree walk found a directory nothing claimed and reported
  **`?` unversioned**: an instruction to `svn add` another working copy.

Neither needed a new mechanism, only the right source. Obstruction is a pure function of
`NODES.kind` against the on-disk snapshot, so it is a rule in `NodeStatusResolver` beside the
others — and only for BASE nodes, because a local tree layer is resolved from presence before the
filesystem is consulted at all. Externals are read from the `EXTERNALS` table and added by
`WorkingCopyScanner`, which also has to *claim* the path and close its subtree; suppressing the
contents alone is not enough, because an unversioned directory closes its subtree too and the
external would still read as something to add.

**Three things only a fixture would have told us.** Built with `svn` and diffed against it:

- **`svn status --xml` reports an external twice** — once as the placeholder, then again with its
  whole contents, because it recurses into that checkout. Kept as-is, one path has two statuses and
  another working copy's nodes leak into this listing. The fallback keeps the placeholder only.
- **The external's placeholder carries no revision.** `svn status -v` prints the columns blank; the
  revision that would mean anything belongs to the other working copy. Both readers answer null.
- **The index only skipped the *root's* `.svn`.** It never mattered while there was one; an
  external brings a second, and the enumeration yields the directory even though it does not
  descend into it.

**The symbol had to move, and that is the user-visible part.** `StatusLine` existed to be diffable
against `svn status` line for line, and its own comment said `~` was free "because Subverted does
not model obstructed". Modelling it makes that false, so `~` went back to SVN's meaning and the
invented state — `NeedsPristineCompare`, which SVN has no equivalent for — took `*`, a character
SVN never writes in column one. Its footer changed with it, and obstruction got a footer of its
own: one is a file we cannot hash, the other is a broken working copy, and they want different
advice.

*Status: implemented and verified. `tools/compare-with-svn.sh` on a fixture carrying both
obstruction directions and an external diffs **identical** to `svn status --no-ignore`, and
`sv st -v` matches `svn status -v` column for column including the external's blank revision.
Reverting any one of the three guards turns tests red.*

### D17 — The generation says *rescan*; the path says *what to redo*

D5 had the session trust a counter and nothing else, and that is what made overflow safe. The cost
was that saving one file in a 100k-node checkout re-read all 100,000 — about a second on a fresh
tree, two once every mtime has moved.

The counter keeps its job. `IChangeNotifier` now also reports *where*, and that path is a hint
about what to redo, never the thing that decides whether the index still stands. Two rules keep
D5 intact:

- **A notifier that cannot name the path says so.** An overflow raises
  `WorkingCopyChange.Unknown`, and the session then rescans rather than re-resolving the paths that
  happened to arrive — which would silently miss whatever was in the discarded buffer.
- **The generation moves under the same lock as the path.** Bumping it outside would let a reader
  see the new number while the set that explains it is still empty, and conclude — correctly by its
  own lights — that nothing needed doing. Inside the lock the pair is always consistent.

**The rule for when it applies is deliberately narrow: every changed path must already be a node
the held scan knows as versioned.** Anything else rescans. That is not caution for its own sake —
a path the last scan never saw changes the *set* of nodes, and the set is decided by ignore-rule
inheritance and unversioned-subtree closing, which are whole-tree properties. Recomputing them
from one path lists files that should be hidden, or hides files that should be listed. So a new
file, a rename, an external, an `svn` operation and an overflow all still cost a full scan; editing
and deleting tracked files — the thing that actually happens all day — does not.

Two things fell out of that rule rather than needing code:

- **`.svn` needs no special case.** Anything SVN itself wrote is a path no scan holds as a node, so
  it fails the rule for free. That also keeps the knowledge where it belongs: the daemon never has
  to know what `.svn` means.
- **wc.db is what refuses.** An unversioned, ignored or external path has no NODES row, so the
  single-node read comes back empty. A guard on the held status was written first and then deleted
  — a mutation test showed it could never fire.

`WcDbReader.ReadNode` is the single-node read this needed and did not have; it is the same
projection with one more predicate.

**Measured on the 101,001-node fixture**, Release, warm daemon:

| | daemon time |
|---|---|
| full scan, cold | ~983 ms |
| warm, nothing changed | ~1.3 ms |
| **one tracked file edited** | **3.4 – 9.3 ms** |
| a new file appears (falls back) | ~728 ms |

The work is now one cheap pass over the held entries plus a stat and maybe a hash per changed
path — so it is still O(nodes), but in hash lookups rather than in `open` syscalls and SHA-1. That
is the ~100× and it is also the ceiling: at a million nodes the pass itself starts to show, and the
fix then is an index the session keeps rather than one it rebuilds.

The CLI fallback gets none of this. It would need a child process per path, which is worse than
rescanning, so it is handed no incremental reader and behaves exactly as before.

*Status: implemented and measured. Answers verified identical to a full rescan of the same tree,
both by an integration test that diffs the two and by hand on a real fixture through the daemon.*

### D18 — Cold, the pristine compare's advantage is a file-cache artifact

D15 measured a byte compare against the pristine at ~1.6× the speed of hashing and could not say
whether that survived a cold cache, because the 1.59 GiB fixture fits in 61.6 GB of RAM. It was
named as the measurement D7 turns on. It has now been taken.

The tree cannot be made bigger than RAM on this machine — 77.8 GB free against 61.6 GB of RAM, and
a working copy carries its pristines too. So the cache is evicted instead: opening a file with
`FILE_FLAG_NO_BUFFERING` drops its pages, and `tools/measure-pristine-compare.cs` does that to every
working file and every pristine before each timed pass. **That the eviction works is measured, not
assumed** — it is what the read-only rows are for.

Four three-run sets, Release, 8 cores / 16 threads, 1,000 files, 1.59 GiB. Two of the four were
taken while a build and the test suite were competing for the machine, and that turned out to
matter enough to report separately rather than average away. The idle sets:

| | warm | cold |
|---|---|---|
| read only, no hashing | 106 – 160 ms | 428 – 537 ms |
| hash the working file *(what we ship)* | 739 – 828 ms | 1,074 – 1,215 ms |
| byte-compare against the pristine | 283 – 372 ms | 990 – 1,187 ms |

Read-only going from ~13 GiB/s to ~3.5 GiB/s is the check that the eviction is real; nothing else
here means anything without it.

**Warm, the compare wins by ~2.3×. Cold, the ranges overlap and what is left is inside the noise.**
The arithmetic is straightforward once seen: cold, both paths go disk-bound, and the compare reads
3.17 GiB where hashing reads 1.59 GiB. Doubling the I/O costs about what the cheaper per-byte work
saves. D15 suspected exactly this — the second read is "free only while the tree is in the OS file
cache" — and it is now a number rather than a caution.

**The loaded sets say something the idle ones cannot, and it is the one result that favours the
compare.** Under contention, cold hashing degraded to 1,296 – 1,659 ms while the cold compare
stayed at 1,100 – 1,273 ms, barely moving. That is the signature of the two being bound on
different resources: hashing competes for cores with whatever else is running, and the compare is
waiting on the disk either way. An artist's workstation mid-export is the loaded case, not the idle
one. It is a real effect and it is still not worth a second content path — an at-best ~1.3× on the
cold scan, which D17 already made rare, against carrying two ways to decide whether a file changed.

So the ~1.6× that reopened D7 is mostly a property of the measurement rather than of the design —
and what survives of it is the warm case, which is precisely the one a daemon by construction
mostly does not pay: a warm scan settles on mtime and reaches neither path. **The recommendation is to leave D7 alone**, and it is a recommendation
rather than a decision because PLAN.md lists it as one for a human. Three things now argue the same
way:

- cold and idle there is no speed to win, and cold under load there is at best ~1.3×;
- hashing has to stay regardless, because SVN 1.15's `--store-pristine=no` makes an absent pristine
  a supported configuration (D15), so a compare is a second path and not a replacement;
- a second path is a second thing to keep correct, on the code that decides whether someone's work
  is about to be committed.

What would change the answer is a slower disk rather than a faster one: the gap closes because
reading is expensive, so on spinning storage the compare gets *worse*, and on storage fast enough
to make both paths CPU-bound it approaches the warm number. This machine's NVMe sits where the two
effects cancel. A studio on a network drive is the case nobody here has measured.

*Status: measured, nothing changed in the code, D7 left standing. The harness is kept next to the
other measurement tools so the number can be re-taken on other hardware, which is where it would
actually differ.*

### D19 — Writing goes through the daemon, and the daemon drops its own index

`sv add`, `sv commit` and `sv revert` are the first commands that change a working copy, and they
take the same route log and diff already took (D6): the CLI speaks `Protocol` only, `Subverted.Svn`
runs the client, and the daemon takes each as a delegate so its dispatch is testable without `svn`
on PATH. The front-end referencing `Subverted.Svn` would have been shorter and is the line
ARCHITECTURE forbids — two implementations of "what changed" is how TortoiseSVN got slow, and two
implementations of "what gets committed" is worse.

Three rules live in the daemon rather than in the CLI, because a GUI will need them too:

- **One request, one working copy.** The first path resolves to a root and every other path is
  checked against it. A set spanning two checkouts would commit half of each and report one
  revision for it.
- **No target is refused, never guessed.** There is no safe default for a command that writes.
- **The held index is dropped the moment the client has run, failure included.** The watcher
  reports these writes too, but not necessarily before the next request arrives, and
  `sv add x && sv st` has to show the add. `svn add` given three paths can schedule two and refuse
  the third, so the drop is in a `finally` — an index that survived that would be wrong about both.

**`sv revert` asks first.** It lists the nodes that would lose work — filtered from the warm status
index, so it costs nothing — and asks; `--yes` is the way to mean it from a script. A redirected
stdin without `--yes` is refused rather than defaulted to yes, because defaulting is how an
unattended terminal reverts a studio's afternoon. Two things the preview has to get right, and both
are pinned: `art` must not claim `artefacts`, and a property-only change reads as `Unmodified` on
the content axis while revert still restores it.

Two things only running it settled, neither of them what the code assumed:

- **SVN re-spells every path in the platform's own separator, whatever it was given.** Passing
  `src/b.txt` on Windows still gets `A  src\b.txt` back — and `svn status` does the same. Every
  `RelPath` Subverted reports is slash-separated, so `sv add` and `sv st` were naming one file two
  ways. The fix is on the output side (`SvnNotification`), not the argument side; the comment on
  `SvnTarget` claiming SVN normalises to forward slashes was simply wrong, and `svn diff` — which
  does normalise — had hidden it.
- **SVN already ends a commit with the revision it created**, so `CommitReport` adds nothing to it.
  The parsed number still travels on `CommitResponse.Revision`, because it is what tells "nothing
  to commit" from "committed" and it is what a GUI will want as a number rather than as a line.

`svn commit` takes the message through `--message` and never an editor: the daemon has no terminal
to open one in, so `sv commit` requires `-m` and says so rather than pretending otherwise.

*Status: implemented. All four test binaries green (867), and driven end to end against a real
repository — add, partial commit, the revert confirmation gate in both directions, and the
no-op cases. The partial commit is what M3's file-level staging will be built on: naming one path
sends that path and leaves the rest local, which is now a test rather than an assumption.*

### D20 — File-level staging is a front-end walk over a commit that stops at the nodes it names

`sv pick` walks the changed nodes one at a time, `git add -p`'s interaction with a whole file per
prompt, and commits what was said yes to. M3 lists it under local history, and it is here early
because it turns out to need none of it: the picked set is just a commit, and D19's
"`sv commit PATH...` sends only what is under those paths" was already the mechanism.

**What it did need was one bit the existing commit could not express.** `svn commit` is recursive,
so a picked directory would have taken the children its owner had just declined. The scope travels
as `CommitScope` — `WholeSubtree` for `sv commit`, `ExactlyTheseNodes` for a picked set, which is
`--depth empty`. It is an enum and not a `bool` because the two are different operations, and it
defaults to `WholeSubtree` so a front-end that has never heard of scopes gets SVN's own behaviour.

**Nothing new was added below the CLI.** `Subverted.Svn` gained an argument, the daemon's delegate
gained a parameter, and the picker itself is `Subverted.Cli` talking to `Protocol` — no new request
type, no second path that decides what gets committed.

Three rules SVN imposes on directories, all read off `svn` 1.8.15 rather than out of the
documentation, and all pinned in `SvnWriteIntegrationTests`:

- **A child whose added *or replaced* parent is not in the same commit is refused outright**
  (E200009, "is not known to exist in the repository and is not part of the commit"). So declining
  such a directory is not a choice about the directory — it removes the subtree from the walk.
- **Named together, they go together.** The parent add and one of its two children commit fine in
  one call; the sibling nobody picked stays local. That is the shape every picked set has.
- **A deletion is recorded on the directory.** Naming a deleted directory removes its whole subtree
  even at `--depth empty`, and naming a deleted child alone exits zero and sends *nothing*. So its
  children follow it whichever way it was answered, and are not offered as a choice they are not.

A replaced directory is both at once, which is the case that stops the first two rules from
collapsing into one: committing it carries away the deletions of what it replaced and leaves the
newly added children still to be decided.

**The walk is split from the terminal, and that was not tidiness.** `ChangePicker` is handed
answers and never reads a console; `PickConversation` is the loop, over an `IPrompt` of three
members. What that buys is the case nobody checks by hand — typing a typo at the prompt. It
explains the letters and asks the *same* node again, because moving on either way decides a file on
the strength of a slip. `sv pick` refuses a redirected stdin rather than defaulting, for D19's
reason in the other direction: there is no safe default answer to "send this?".

Two things left deliberately:

- **Nodes `svn commit` cannot take are not offered** — conflicted, missing, obstructed,
  unversioned. Naming one fails the whole commit. They are not announced either; `sv st` shows
  them and a picker that lectured about each would be a picker people stop reading.
- **No `sv pick --yes`.** A picker with the asking removed is `sv commit`, which already exists.

*Status: implemented. All four test binaries green (983), including the two directory rules against
a real repository. Driven end to end against a real checkout through the shipped `sv.exe` and
daemon: the picked set committed exactly `art/hero.txt` and `src/new.txt` at r2 while `src/a.txt`
and `src/b.txt` stayed local, and `sv st` afterwards matched `svn status` on the same tree.*

**The one thing not run: the interaction itself.** `sv pick`'s refusal without a terminal was run;
the walk with somebody answering it has never been. This machine has no console to give a child —
a ConPTY driver was written to get one and abandoned when it turned out the session has no console
at all, which is why `PickConversation` exists as something a test can drive instead. Every
question, answer and re-ask is a test; no human has seen the prompt.

### D21 — `svn update` reports failure in its text, not in its exit code

`sv up` brings the rest of the studio's work in, and it takes D19's route exactly: `Protocol` in the
front-end, `SvnUpdateCommand` in `Subverted.Svn`, a `BringUpToDate` delegate on the daemon so
dispatch is testable without a server. It goes through `WritingAsync` with the other three, because
an update *is* a write — it rewrites the files the held index describes, and drops that index the
moment the client has run.

**The reason it needed a parser at all is one measured fact: `svn update` exits zero on a conflict.**

```
Updating '.':
C    src/a.txt
U    src/b.txt
Updated to revision 3.
Summary of conflicts:
  Text conflicts: 1
```

That is exit code 0. Text, property and tree conflicts all print that block and all exit zero, so
`svn up && build` compiles a file full of `<<<<<<<`. The exit code says the client ran; the only
statement of what happened is the text. So `SvnUpdateOutput` reads the counts, `UpdateOutcome`
carries them, and **`sv up` exits 4 (`NeedsAttention`) when the update left something for a human** —
a deliberate divergence from `svn`, and the one place Subverted does not mirror its exit codes.

It describes *that update*, not the tree. A second `sv up` over an already-conflicted working copy
exits 0, because SVN counts what this run produced and this run produced nothing. `sv st` is what
answers "is my tree clean".

Four more things read off `svn` 1.8.15 rather than guessed, all pinned in
`SvnUpdateIntegrationTests`:

- **"At revision N" and "Updated to revision N" both mean the working copy stands at N.** SVN prints
  the first when nothing came down — which includes a *tree conflict*, where the conflict blocked
  everything. Reading only "Updated to" would report no revision for every up-to-date working copy
  in the studio, and for the tree conflicts as well.
- **A path SVN will not touch is `Skipped`, and that is also exit zero.** A mistyped target looks
  exactly like a successful update. It is counted separately from conflicts rather than folded in,
  because nothing was merged and no file holds markers — telling somebody to resolve a file SVN
  never opened is a different wrong answer. In practice the daemon catches this first: a path in no
  working copy fails the session lookup, so `sv up /some/typo` is a named error and exit 1 where
  `svn update /some/typo` is silent success.
- **Several targets are updated independently, each with its own revision**, under a
  `Summary of updates:` block. So `sv up` takes at most one path — with two, "what revision is this
  now" has two answers and nothing to pick between them.
- **`--accept postpone` is stated rather than left to `--non-interactive`'s default.** A daemon
  picking a side of somebody's conflict is the one outcome nothing here can undo, and a default that
  moved under us would do exactly that without a line of this changing.

`SvnUpdateOutput` matches the four summary labels wherever they appear rather than tracking the
heading above them. Every other line an update prints begins with a status column or a quoted path,
so a file really named `Text conflicts: 9` arrives as `A    Text conflicts: 9` and cannot be read as
a count — the "only after the heading" guard would have been a branch no real input could reach.

*Status: implemented. All four test binaries green (1031), including ten cases against a real
repository with a second checkout standing in for a teammate. Driven end to end through the shipped
`sv.exe` and daemon: a clean update brought three of a teammate's changes in and `sv st` afterwards
agreed with `svn status`; a conflicting one printed SVN's own text, added the count, exited 4 and
left `.mine`/`.rN` beside the file; `sv up src` updated that path and left `art` where it was.*

### D22 — A lock that was not granted is not a failure, and that is the whole problem

`sv lock` and `sv unlock` are the third thing principle 4 lists under what an artist needs, after
get-latest and send-work, and the first two shipped without them. They take D19's route:
`SvnLockCommand` in `Subverted.Svn`, `AcquireLocks` and `ReleaseLocks` as daemon delegates,
`WritingAsync` around both — a lock token lands in wc.db, so `sv st` reads it back as the `K` it has
shown since M0, and an index that outlived the client would answer "you hold this" about a lock that
had just been refused.

**D21's trap again, and worse. `svn lock` exits zero after refusing every path it was given.**

```
$ svn lock art/hero.png                                              # exit 0
svn: warning: W160035: Path '/art/hero.png' is already locked by user 'ada' in filesystem '…'
```

Nothing on stdout. A warning on stderr. Exit 0. So `svn lock x && open x` hands an artist a file
somebody else is painting, and on a `.psd` there is no merge at the end of that — one of the two
afternoons is thrown away. Three refusals were measured and all three behave this way:

- **W160035**, somebody else holds it. The only line in the whole system that names *who*.
- **W160042**, `Lock failed: newer version of '/x' exists` — the file moved on since this working
  copy last updated. Nobody expects a lock to need an update first.
- **W160040**, `No lock on path '/x'` from `unlock`: the lock had already gone. This is how somebody
  finds out theirs was stolen, and SVN drops the local token either way.

So `SvnWarnings` reads stderr, `LockOutcome` carries the refusals beside SVN's own text, and
**`sv lock` and `sv unlock` exit 4 (`NeedsAttention`) when anything was refused** — the second and
third places Subverted deliberately does not mirror `svn`'s exit codes. The warnings are passed
through verbatim rather than re-worded: SVN's wording carries the holder's name and the repository
path, and a warning code this build has never seen still prints as something a human can read.

A refusal is one line starting `svn: warning:`. An `svn: E…` line is *not* counted, because it comes
with a non-zero exit code that the caller has already turned into a failure — counting it here would
report the same thing twice.

What separates a refusal from an outright failure, also measured:

- **A directory is a failure, not a refusal.** `E155008: … is not a file`, exit 1. SVN locks files
  and only files, and `lock` does not accept `-R`. So `sv lock` has no default target at all: the
  only guess available is the current directory, and a directory is the one thing that cannot work.
- **`unlock` validates locally first, and it is all-or-nothing.** One path this working copy does not
  hold fails the whole command with `E195013` and releases *none* of the others. A caller reading
  that as "some of them went" would be wrong about every one.
- **Stealing works and the loser does not find out.** `--force` takes the lock, exits 0, and the
  other working copy goes on printing `K` until it updates — at which point `svn update` prints `B`
  for the broken lock and clears it. Nothing on this side can fix that; the stale token lives in
  somebody else's wc.db. `--steal` and `--break` are spelled out on the command line for that reason,
  and they are separate words because you steal a lock by taking it and break one by releasing it.

`sv st` still says nothing about locks other people hold — that needs `svn status -u`, a server round
trip per invocation. Until the "lock presence" question in PLAN.md is answered, a refused `sv lock`
is how the studio finds out, which is late but at least it is not silent.

*Status: implemented. All four test binaries green (1097), including thirteen cases against a real
repository with a second checkout standing in for a teammate. Driven end to end through the shipped
`sv.exe` and daemon: a lock taken and `sv st` showing `K` on the next call; a lock a teammate really
held refused with SVN's warning and exit 4; two paths where one went through and one did not; a
steal, a break, a directory, a bare `sv lock`, an `unlock` naming one unheld path that released
nothing, and an `unlock` of a lock that had been stolen — exit 4, local token dropped.*

### D23 — Resolve is silent about doing nothing, and its default depth is why

D21 gave `sv up` an exit code that says "this update left something for a human". It had nothing to
say about what that human should then do, because there was no way to finish a conflict inside
Subverted at all: `sv up` exited 4 and the only way forward was `svn resolve`. `sv resolve` closes
that, on D19's route — `SvnResolveCommand` in `Subverted.Svn`, `ResolveConflicts` as a daemon
delegate, `WritingAsync` around it. Every version but the working one rewrites the file, and the
conflict flag it clears is the `C` the held index read out of wc.db, so the index has to drop.

**Two flags go on every invocation and both are load-bearing.** Neither is a preference.

```
$ svn resolve --accept working sub          # exit 0, prints nothing, resolves nothing
$ svn resolve sub/nested.txt < /dev/null    # still waiting when the test harness killed it
```

- **`--recursive`, because resolve's default depth is `empty`.** Naming a directory settles nothing,
  says nothing and exits zero. That is the same shape as D22's refused lock — a success that did not
  happen — except here nothing at all is printed, so there is not even a warning to read.
- **`--accept`, because without it `svn resolve` waits on stdin forever.** Not "reads EOF and
  gives up": stdin at end-of-file, and it was still waiting when a 20-second timeout killed it. A
  daemon that ran that would hang holding the request. With `--non-interactive` it is instead a
  clean `E205000: invalid 'accept' ARG`, which is why that flag is passed as well even though an
  explicit `--accept` already makes the prompt unreachable.

Fixing the depth is what makes the silence honest, and that is the point of doing it rather than
documenting it. Once `-R` is always passed, "no lines printed" can no longer mean "did not look", so
`ResolveReport` can say **`nothing was conflicted`** and have it be true. Without the flag the same
words would be a lie about a directory full of conflicts.

**A refusal here exits non-zero, unlike a lock's — and is still per-path.** `svn unlock` validates
every target up front and releases *none* of them if one is wrong; `svn resolve` settles the good
targets, reports the bad one, and exits 1. So the exit code cannot be read as "nothing happened",
and `SvnResolveCommand` lets the warnings win over it: non-zero *with* warnings is an outcome,
non-zero *without* them is the client itself failing and throws. Four refusals were measured:

- **W155027**, `Tree conflict can only be resolved to 'working' state`. The one a person actually
  hits — a local edit against an incoming delete takes the working version or nothing.
- **W155010**, the node was not found, and **W155007**, the path is in no working copy.
- **W155009**, `Failed to run the WC DB work queue` — *the file is open in something else.* This is
  the studio case: the artist still has the `.psd` in Photoshop, so SVN cannot write over it. It
  also **leaves the working copy wedged** — the next `svn status` fails outright with `E155037`
  until `svn cleanup` runs. SVN's wording says so, which is the argument for passing warnings
  through verbatim rather than re-wording them.

`sv resolve` exits 4 when anything was refused, the fourth place Subverted deliberately does not
mirror `svn`'s exit codes.

**`--working` marks a node resolved with the merge markers still in it.** SVN does not read the file
to check, so this is `svn resolve --accept working` on a file full of `<<<<<<<` reporting success,
and then a commit that ships it to the whole studio. `ResolveReport` prints a line saying to check —
and says *why*: nothing looked inside the files. It cannot be narrower than that, because SVN's
notification is word-for-word identical for a text, a property and a tree conflict, so nothing in
the output distinguishes a node that could have markers from one that cannot.

**Which versions ask first is a decision, not a default.** `--theirs` and `--base` discard what this
working copy had, and for an edit nobody has committed there is no second copy — so they take
`sv revert`'s route: list the conflicted nodes, ask, refuse a redirected stdin rather than assume
yes, `--yes` to mean it from a script. `--mine` and `--working` keep the local side and do not ask;
what `--mine` drops is the incoming revision, which is still in the repository. There is no default
version at all, for the same reason `sv lock` has no default target: SVN's own default here is to
ask a human, and the daemon has no terminal to ask in.

*Status: implemented. All four test binaries green (1188, up from 1097), including fourteen cases
against a real repository with a second checkout standing in for a teammate — among them the
directory target that proves `-R` is passed, the tree conflict refusing all three other versions,
a bad target that left the good one resolved, and the file held open by another process. Driven end
to end through the shipped `sv.exe` and daemon: `sv up` into a text conflict at exit 4, `sv resolve`
with no version refused, `--theirs` refused for want of a terminal and then run with `--yes`,
`sv st` clean afterwards with SVN's leftovers gone, a second resolve reporting `nothing was
conflicted`, `--working` warning about markers that really were still in the file, and a tree
conflict refused at exit 4 and then settled with `--working`.*

### D25 — Cleanup says nothing at all, so the report is read out of wc.db instead

D23 found the state by accident: resolving a file still open in the application that owns it leaves
the working copy wedged, and `svn status` then fails outright until `svn cleanup` runs. That left
the escape hatch as the only route out of a state a studio reaches without trying. `sv cleanup`
closes it, on D19's route — `SvnCleanupCommand` in `Subverted.Svn`, `CleanUpWorkingCopy` as a daemon
delegate, `WritingAsync` around it — and the index drops afterwards, because what cleanup clears is
the state that was making every other write fail.

**There are two wedged states and they behave nothing like each other.** Both are cleared by the
same command, which is the only thing they have in common:

| | mechanism | `svn status` | `svn` errors with |
|---|---|---|---|
| write lock | a `WC_LOCK` row | **works**, prints `L` in column 3 | `E155004` on every write |
| unfinished operation | a `WORK_QUEUE` row | **fails, exit 1** | `E155037`, status included |

**The row a crashed client leaves is the root at every level**, `('', -1)` — measured by parking an
`svn commit` in a pre-commit hook that never returns and killing it, which is also how
`SvnCleanupIntegrationTests` reproduces it rather than racing the commit's own speed. A client that
*fails* gracefully leaves nothing at all: SVN unwinds its own lock on a clean error exit, so
`E720033` from a file held open by another process wedges nothing. It has to die.

**`svn cleanup` writes nothing to either stream, on any outcome.** No stdout, no stderr, exit zero,
whether it unwedged a working copy or found nothing wrong. That is D22 and D23's shape one notch
worse — there is not even a warning to read — so there is no output to parse and the report cannot
come from the client at all. It comes from reading wc.db on both sides of the run and taking the
difference: `PendingCleanup.Read` before and after, `CleanupEffect.Of` between them. The diff is
pure and is where the rules are tested; the two reads are the I/O either side of it.

**Cleanup runs at the root, whatever path was named**, because its two halves scope differently and
measuring that was the surprise. `svn cleanup sub` drains the **whole** work queue — `WORK_QUEUE`
carries no path column — but releases only the write locks reaching into `sub`, so a lock on the
root at depth 0 survives it. A scoped run can therefore exit zero having left the copy locked. This
is `sv revert`'s `--depth infinity` and `sv resolve`'s `--recursive` a third time: SVN's own scoping
quietly does less than the target people type implies.

**`locked_levels` is a count of levels below the locked directory**, and nothing documents it. A
lock on `sub` at `1` made `svn status` print `L` on `sub` and `sub/deep` and not on
`sub/deep/deeper`; `-1` is every level, `0` is that directory alone. `WriteLockCoverage` is that
rule as a pure function, because one row lights up many directories and the rows cannot be matched
to entries by path.

**Which turned up a third divergence in `sv st`, and unlike the two M0 records it under-reports.**
Column 3 was left blank deliberately — `StatusLine` said so — but nobody had measured what that
costs, and the answer is that a working copy every write is about to fail on read back as perfectly
clean. Both scanners now answer it: wc.db through `WC_LOCK`, and the CLI fallback through
`svn status --xml`'s `wc-locked="true"`, so which reader answered stays invisible. `StatusFilter`
had to learn it too — a write-locked directory is otherwise unmodified, so the filter was about to
drop the one line that mattered.

The rule is **directories only**, and taking `NodeKind` rather than deciding on the path is not
defensive: a file's path sits under a locked directory exactly the way a locked subdirectory's does,
so the path-only version reported every file in a wedged working copy as locked. It was written
that way, and `A_file_under_a_write_locked_directory_is_not_itself_locked` is what caught it.

**Cleanup asks first**, which no other read-shaped command does. `svn help cleanup` carries a
corruption warning — it cannot tell a lock left by a dead client from one a live client is still
using — so this takes `sv revert`'s route: warn, ask, refuse a redirected stdin rather than assume
yes, `--yes` to mean it from a script. There is nothing to preview the way revert and resolve
preview, because the working copy this is aimed at may be one `svn status` itself will not read.

One thing left open rather than answered: **`sv st` still answers on a `WORK_QUEUE`-wedged copy
where `svn status` refuses to.** The fast path reads the tables it needs and never looks at the work
queue, so it reports a tree that SVN considers mid-operation. `sv cleanup` fixes the state and says
so, but status does not warn, and making it warn means the scan carrying a working-copy-level fact
that `IWorkingCopyScan` has no shape for. Closed by D26.

*Status: implemented. All four test binaries green (1280, up from 1205), including eleven cases
against a real repository — among them a client really killed mid-commit, the `E155004` a write lock
causes and its disappearance afterwards, the `E155037` that stops `svn status` dead, both states
cleared in one run, a cleanup that fails part-way and the lock it leaves behind, and an assertion
that cleanup's output really is empty on the run that unwedges the copy. Driven end to end through the shipped `sv.exe` and daemon against a genuinely crashed
working copy: `sv st` printing `  L     .` character-for-character with `svn status` where it used
to print nothing, `sv cleanup` refused for want of a terminal, then `--yes` releasing the lock, then
a second run reporting `nothing needed cleaning`. A cleanup that failed part-way — a work item whose
file had no pristine — was also seen, and left a write lock of its own behind, which is why the
report carries what remains as well as what went.*

### D26 — A capability the fallback does not have, rather than a member it answers dishonestly

D25 closed one half of the under-reporting it found and named the other half in the same breath.
A `WC_LOCK` row leaves `svn status` working and printing `L`; a `WORK_QUEUE` row stops it dead at
`E155037`, and Subverted answered that tree anyway — its fast path reads `NODES`, `ACTUAL_NODE` and
`WC_LOCK`, and the work queue is none of them. So the one state in which a person can see *nothing*
through `svn` was the state `sv st` was most confident about.

The reason it did not ship with D25 is that the two facts are shaped differently. A write lock is a
fact about a **directory**: it goes on `WorkingCopyEntry` beside the rest of the node's status and
both scanners can answer it. Unfinished work is a fact about the **working copy**, and only one
reader can see it at all — on the CLI fallback the wedge is not a field, it is the scan failing.

So it is an optional interface, `IUnfinishedWorkScan`, exactly as `IIncrementalScan` already is:

```
IWorkingCopyScan      Info, ScanAsync            — both readers
IIncrementalScan      TryApplyAsync              — wc.db only; the fallback would need a process per path
IUnfinishedWorkScan   CountUnfinishedOperations  — wc.db only; the fallback fails instead of counting
```

`WorkingCopySessionFactory` hands the session `scan as IUnfinishedWorkScan` beside the cast it
already makes, and a session that gets `null` reports zero. That zero is the honest answer for a
reader that cannot look — and the reason it is not a lie is that on that reader a wedged copy never
reaches the question, because `svn status` failed first.

**The count is read per scan, under the same lock.** Not once at open: a client can wedge a copy the
daemon is already holding, and `sv cleanup` clears it while the daemon still holds it, so a cached
value is wrong in both directions. Not inside `Scan()` either, because the incremental path skips
`Scan()` — it re-resolves a handful of paths, and a count folded into the scan would go stale
exactly where the daemon is fastest. It is read in `CurrentAsync`, after the scan-or-update and
before the result is stored, which also puts it on the one thread allowed to touch the SQLite
connection. `WorkingCopyScanner` owns that connection, so the read goes through it rather than
through `WcDbReader` directly — the row shape stays this assembly's private business.

**The warning leads the listing.** `sv st` already prints explanatory notes, and they trail: they
say what one letter on one line means. This one says every line below it is Subverted's reading
alone, which is not something to discover after scrolling.

**The exit code does not move.** `sv st` describes rather than judges — it exits zero on a tree full
of conflicts — and `NeedsAttention` means a command ran and left something behind. A script that
wants to act on this reads `UnfinishedOperations` off the wire.

*Status: implemented. All four test binaries green (1296, up from 1280). The mutation that matters
is deleting the `scan as IUnfinishedWorkScan` cast: every fake-driven session test stays green,
because they hand the session its own reader. What goes red is the end-to-end case — a real working
copy, a real wedge, a real socket — which is why that one exists. Driven through the shipped
`sv.exe` and daemon as well: `svn status` exiting 1 having printed nothing, `sv st` printing the
warning and then `M art/hero.png`, `sv cleanup --yes` reporting one operation finished, and both
tools then agreeing the copy is clean. Worth knowing from that run: cleanup **runs** the queue
rather than discarding it, so the queued `file-install` reinstalled the pristine over the modified
file. That is SVN's own behaviour and it is the concrete form of the corruption warning D25 quotes —
finishing an interrupted operation can move working-file bytes.*

### D27 — A rename is a fact about a pair, and SVN cannot see one it was not told about

`sv` could add, commit, revert, update, lock, resolve and clean up, and could not delete or rename.
That is the operation an asset tree gets most: an artist renames `hero.png` in Explorer, and SVN
sees an unrelated `!` and `?`. Committing in that state records a delete and a fresh add, and the
file's history stops at the old name — silently, with no error anywhere.

**`svn move` cannot repair one, and says so badly.** Measured on 1.8.15, three quite different
situations report as `E155010: Path '…' is not a directory`, because SVN reads a two-argument move
with a non-directory destination as "move *into* that":

| source on disk | destination on disk | `svn move` |
|---|---|---|
| yes | free | works |
| yes | occupied | `E155010`, nothing touched |
| no | occupied | `E155010`, nothing touched |
| no | free | **prints `A dst`, exits 1, leaves *both* paths missing** |

That last row is the dangerous one and it is why the route is decided before the client runs rather
than by reading its complaint. So `MoveRouteResolver` is a pure function of two filesystem facts,
and `MoveRefusal` turns the three refusing routes into sentences that name the path in the way.

**Detection is by content, and only when it is unambiguous.** wc.db records a SHA-1 per node, so a
missing node already has its fingerprint and only the unversioned candidates need hashing. A digest
two nodes share is dropped from *both* sides: duplicate content makes "which file became which" a
guess, and a wrong guess tells someone their asset moved somewhere it did not. Names are not
consulted at all — `hero.png` → `protagonist.png` shares no characters, and a file overwritten by a
different one of the same name shares all of them.

**It costs nothing on a working copy with nothing missing.** The pass returns immediately when no
node is missing, and otherwise hashes only unversioned files whose size on disk matches a size some
missing node recorded. Without that filter an ordinary scan would pay for whatever build output
happens to sit in the tree.

**The pairing is a capability, exactly as D26's count is.** It is against the checksum wc.db
recorded, and the CLI fallback never sees one — so `IUnrecordedMoveScan` joins the two optional
interfaces rather than becoming a member the fallback answers dishonestly. Unlike D26's count it is
computed *from the entries the scan just produced*, which is what keeps it off the tree walk: the
missing and unversioned nodes are already decided by the time it runs. It is recomputed on the
incremental path too, because that path can turn a node Missing — one half of a rename — and
carrying the old answer forward would hide the pair exactly as it appears.

**The repair never compares content, so it cannot lose any.** `svn move` needs its source on disk,
so the renamed file is moved back to the old path and `svn move` carries it to the new one. *(As
first built it set the file aside, restored the source with `svn revert`, moved, and put the file
back — D28 replaced that, see below.)* The bytes the person had are the bytes they end up with, and
a file renamed *and* edited keeps the edit; `svn status` then prints `A  +` for it like any move,
and the edit shows in `svn diff`.

**`sv rm` always passes `--force`, and asks first because of what that means.** Without it SVN
refuses a modified file (`E195006`) and an unversioned one (`E200005`) — the two cases the
confirmation is about. The second is the one worth stating plainly: **an unversioned file has no
pristine, so `svn delete --force` unlinks it, prints nothing, and exits zero.** Nothing brings it
back. The preview splits its targets on exactly that line and counts the unrecoverable ones
separately, and the report says how many went that way, because SVN's own output for them is silence.

**A lock does not follow a rename.** Measured: after moving a locked file, `svn status` shows
`D    K  art/villain.png` — the token stays on the path that now exists only to be deleted, and the
file is unlocked under its new name with nothing saying so. `sv mv` reads the lock before the move
and warns after it.

*Status: implemented. All four test binaries green (1414, up from 1296). Driven through the shipped
`sv.exe` and daemon on a real working copy: `svn move` refusing the hand-rename with the "not a
directory" message and exiting 1, `sv mv` recording it, and `svn log` on the new name then showing
`A /art/protagonist.png (from /art/hero.png:1)` and reaching back to r1 — which is the whole claim.
All three refusals print their explanation and exit 1, and the lock warning fires with `svn status`
agreeing the `K` stayed behind.*

*One bug that run found and no test had: `sv rm` on a **clean** versioned file said "nothing to
remove" and did nothing. The preview is built from a status listing, and a file nobody has edited is
absent from a default one — so the commonest removal of all was the one that silently failed. The
switch choice now lives in `RemovalPreview.ListingFor` where a test can hold it, rather than as a
literal in `Program.cs` where nothing could.*

*This closed by noting that `sv st` printed `A ` where `svn status` printed `A  +`, column 4 having
been blank since M0. Chasing that turned out to be a much larger thing than a column — see D28.*

### D28 — op_depth is the depth of the operation, not a flag that says "added"

D27 left one visible gap: `sv mv` produces `A  +` every time and `sv st` printed `A `. Filling the
column meant reading what wc.db says about copies, and that showed the column was the least of it.
**The resolver sent every op_depth > 0 row to one branch that never looked at the disk**, so
everything inside a copied directory read as `A` whatever had happened to it:

| inside `dircopy/`, copied from `dir/` | `svn status` | `sv st` before |
|---|---|---|
| untouched | *(not listed; `   +` under `-v`)* | `A` |
| edited | `M  +` | `A` |
| **deleted from disk** | **`!`** | **`A`** |
| property set | ` M +` | `A` |
| deleted with `svn rm` | `D  +` | `D` |

Two more were wrong in the other direction: a replace *inside* a copy (`R  +`) read as `A`, because
the rule asked whether a BASE row existed and there is none; and the child of a directory replaced
by a copy (`   +`) read as `R`, because it has one. `sv mv art/ gfx/` on a folder of two thousand
assets listed two thousand `A` lines.

M0 recorded `A  +` as verified. It was not: `compare-with-svn.sh` reads columns 1 and 2 only, and
the fixture had a copied *file* — the one shape the old rule answers correctly. The script reads
column 4 now, and `SvnFallbackIntegrationTests.EveryShape` carries a copied directory with one of
every change inside it, which is the test that would have caught this the day it was written.

**The rule, every line of it measured on SVN 1.8.15** against a fixture built for it:

- **op_depth is the depth of the path the operation was aimed at.** A row whose op_depth equals its
  own path's depth *is* an add, copy, move or replace. Below that it is a node the operation carried
  along. Every plain add is a root of its own — `svn add plaindir` puts `plaindir/y.txt` at
  op_depth 2 — so a normal row below its root can only be inside a copy.
- **Disk first, at every depth.** A missing or obstructed node prints `!` or `~` whether it is BASE,
  an add, a copy's root, a replace or a node inside a copy. The one exception is a delete, decided by
  its row alone: `svn delete --keep-local` leaves the file on disk and still prints `D`. An existing
  test asserted the opposite for obstruction — "the ordering is the behaviour, not an accident" — and
  was wrong; it had never been run against `svn`.
- **An operation root is `R` when any layer lies beneath it, `A` otherwise**, and its content is
  never compared: an edited add is still `A`.
- **A node inside a copy is compared like BASE**, on both axes, against the copy's own recorded
  size, mtime, checksum and properties.
- **`+` means the row names a repository node** (`repos_id`; a plain add names nothing), or, for a
  delete, that the layer it deletes is a copy. Deleting a plain add drops its row instead of
  layering over it, so "the layer beneath is above BASE" is exactly "it is a copy". The `+` survives
  a modification and a conflict (`C  +`, measured after a merge) and is dropped for missing and
  obstructed nodes, because what is on disk is then not the copy. `svn status --xml`'s `copied`
  attribute follows the same rule, so the fallback needs no logic of its own.

**The missing row was the one that hid something, and not in the way expected.** It was written
down first as "the commit is about to fail". It does not: a copy is made on the server, so
committing sends the file the artist deleted along with the rest, and the next update puts it back
on disk. Nothing on either side of that says so. `!` is the only warning anyone gets.

**Untouched copied nodes are not "something to report"**, so a default `sv st` hides them exactly as
`svn status` does and `-v` shows them. `StatusFilterTests` holds that. It is safe for `sv pick` too,
and that was measured rather than assumed: a copy commits whole even at `--depth empty` (SVN says
so on stderr), so picking just the copy's root sends everything it carried, while a child's local
edit stays behind as `M` for its own prompt.

**D27's repair had to learn copies.** Before this, no op_depth > 0 node could read as missing, so
the rename detection only ever saw BASE nodes. Now a file renamed inside an uncommitted copy pairs
correctly — and the first repair then failed: `svn revert` refuses a node inside a copy (`E155038:
Can't revert … without reverting parent`) and unschedules a copy's own root, leaving nothing
versioned to move. It failed cleanly with nothing touched, but `sv st` was offering a fix that did
not work.

The repair never needed the pristine at all: whatever sat at the source was overwritten by the
person's file once the move was recorded. So it now moves the person's file back to the old path
and runs `svn move` on it — no revert, no stash, no suffix. Measured on 1.8.15 before it was built:
BASE, edited, `svn:needs-lock` (the property survives), a node inside an uncommitted copy, a copy's
own root, and a move into a newly added folder all record, and all reach r1 in `svn log` once
committed. Two things fall out of it:

- **An interrupted repair leaves the file at its old name**, where it came from, rather than under a
  suffix that had to be refused and explained next time.
- **A folder the person deleted is recreated only for as long as `svn move` needs it.** Moving
  `old/solo.png` out and deleting `old/` is common; the repair creates the missing parents, and
  removes them again afterwards if empty — never recursively, and never one that was already there.
  Whether the deletion of `old/` gets recorded is the person's call, so it reads `!` as before.

Three branches of the repair are not reached by any test, all guards against states nothing can
force on demand: the parent walk reaching the filesystem root (the working-copy root always exists),
a recreated folder vanishing before cleanup, and `svn move` failing after it had already moved the
file. It is an I/O type, covered by integration tests against a real working copy; every line is
reached.

**Cost: one gated subquery.** The lower layer is a correlated `MAX(op_depth)` over the primary key.
Ungated it ran for every row and cost **~100 ms of a 101,001-node read** — median, thirty
interleaved runs, twice, reproduced within 3 ms. BASE has nothing beneath it by definition, so it
is gated on `op_depth > 0` and costs **~10 ms**, inside the noise. Same-session A/B of the SQL alone,
through Python's `sqlite3`, on the `subverted-100k` wc.db; the warm path runs no SQL and is untouched.

*Status: implemented. All four test binaries green (1476, up from 1414). `compare-with-svn.sh`
identical to `svn status --no-ignore` on the `subverted-copy` fixture — 46 lines of 46, 13 of them
carrying `+`, covering every shape above — and every other fixture differs only where it did
before, on nodes at op_depth 0 that this change cannot reach. Through the shipped binaries, `sv mv`
of a directory then an edit and a lost file inside it reads column-for-column like `svn status` and
`svn status -v`. The three resolvers and `WcDbRow` are at 100% line and branch.*

### D29 — Which working copy and what to list are two fields, because the previews need the first alone

`sv st PATH` listed the whole working copy; PATH only chose which one. `svn status PATH...` lists
each target and everything beneath it, the union of several, nothing for a path nothing is at, and
— with no target — the current directory rather than the root. `sv st` does all four now, measured
line for line against `svn status` on the `subverted-copy` fixture.

**The obvious implementation was dangerous.** `StatusRequest.WorkingCopyPath` looks like the thing
to scope by, and four commands — `sv revert`, `sv rm`, and two more — send their *first* target
there, then filter the whole listing for all of their targets themselves. Scoped by that field,
`sv revert a b` would have previewed `a` alone and asked to discard `b` without showing it. So the
scope is its own field, `StatusRequest.Scope`, and **null means the whole working copy**, which is
what every preview keeps sending. Measured afterwards through the shipped binaries: a two-target
revert still lists both.

Scoping happens in the daemon, next to `StatusFilter`, for the reason that type gives: a listing
scoped to one folder should not serialise a hundred thousand nodes for the front-end to throw away,
and M2's GUI needs the same rule without a second copy of it. `TargetCoverage` moved from the CLI
to Core for that: it is pure path logic the daemon and the front-ends both need, and it was
briefly in `Protocol` until that was caught against the rule above. A rename's note is kept when either half of it is in scope, because a
file dragged into another folder leaves one half outside any single target.

**Still different from `svn`: paths print relative to the root, not to the current directory.**
`svn status` from inside `dircopy` prints `c1.txt`; `sv st` prints `dircopy/c1.txt`. That is every
`sv` command's convention, not status's alone, so changing it is a decision about all of them.

*Status: implemented. All four test binaries green (1497). `sv st` matches `svn status` on nine
target shapes — a directory, a nested one, a file, a missing file, an unversioned one, a path that
does not exist, two targets, a moved-away directory and `-v` — and scopes to the current directory
when given none.*

### D30 — The GUI shell is a view of the daemon, kept live by asking, and never blank

M2's first slice: `Subverted.App`, Avalonia 12 with CommunityToolkit.Mvvm, opening a working copy
and keeping its status on screen. It talks to the daemon exactly as `sv` does. What the two share
lives in `Subverted.Frontend` (`DaemonChannel` today: connect, or start the daemon from beside the
binary), so the App references Frontend and nothing below Protocol.

**Live by polling, while in front.** The daemon has no push channel and does not need one for this:
a warm answer is about a millisecond, so the window asks once a second while it is active and stops
when it is not. Ticks never overlap — the next wait starts when the last refresh finishes, so a
cold scan delays the next one rather than queueing a pile. Coming back to the front answers at
once, because the first thing someone sees after switching from their editor should be what they
just saved. `StatusPolling` takes a `TimeProvider`, and every one of those rules is a test on a
fake clock.

**Never blank, never falsely clean.** `IsClean` means an answer came back and listed nothing —
never "nothing has come back yet". A failure after a listing keeps that listing on screen marked
stale (`IsStale`) rather than making someone's changes vanish for the second a daemon takes to
restart; a failure with nothing to fall back on says what is wrong instead (`IsBlocked`). A fresh
listing is merged into the shown one by `ChangeListSynchronizer`, fewest edits, equal rows left as
the same instance — replaced wholesale once a second, the list would flicker and drop its selection.

**Presentation is pure and separate.** `ChangeBadges` turns an entry into a label and a tone (a
conflict outranks the content axis; a property-only change is still a change), `ChangeSummary`
orders the header's counts by what needs a person first, `ChangeRow` splits name from folder. None
touch Avalonia. The view models take their I/O as consumer-named interfaces — `IWorkingCopyStatus`,
`IFolderPicker`, `IRecentWorkingCopyStore` — and the adapters in `Infrastructure/` are the only
code that knows a socket, a dialog or a file exists.

**The look.** Design tokens are theme-variant resources, dark and light, with the accent taken from
the operating system as WinUI does. Surfaces are translucent over the window, so on Windows 11 they
read as layered glass over Mica — `TransparencyLevelHint="Mica, AcrylicBlur, None"`, content
extended into the title bar, and WinUI composition for the rounded backdrop — and over the solid
fallback they are plain tinted panels. Glass edges are a top-lit gradient border; controls are
capsules; motion is 120–180 ms CubicEaseOut and there are no page cross-fades, which oto found read
as lag. Icons are stroked geometry resources, because the icon package oto uses is still built
against Avalonia 11.

**macOS's Liquid Glass is designed for and not built.** The styling is the glass language on both
platforms, and extending the client area gives a full-size content view with the traffic lights
over the sidebar's top strip. Real Liquid Glass is `NSGlassEffectView`, which needs native interop
that has to be built and run on the macOS runner — and this checkout is not a git repository, so
`git push mac` has nothing to push. Nothing macOS-specific is claimed to work.

**Seeing it without a display.** `MainWindowRenderTests` builds the real window around fake data,
renders it through Skia headless, asserts on the visual tree, and saves the frames — dark, light,
clean, unreachable, first run. Looking at them caught three things no assertion did: a badge wider
than the rest pushing one name out of line, a header showing an empty path because a fallback value
cannot be a binding, and a section heading over an empty list.

*Status: slice 1 implemented. All seven test binaries green; the App's presentation rules, view
models and converters are at 100% line and branch, and its adapters are covered by integration
tests against a real filesystem and an absent daemon.*

**Then it was looked at, and running it found two things no test had.** The first capture hit a
locked workstation. Once unlocked, the published app, launched on a fixture, showed:

- **An opaque title strip over the extended client area** — Avalonia 12 draws its own decorations
  on Windows, and Fluent's theme for them paints a background, the window title and a fullscreen
  button across the top 30 px, over the sidebar. `Window.WindowDecorationsTheme` is the extension
  point for exactly this: `Themes/Decorations.axaml` is Fluent's template (12.1.3) with the strip
  transparent, no title line, no fullscreen button, and 48 px caption buttons to match the title
  row. The roles that make minimise, maximise and close behave as system buttons are kept as they
  were. Recaptured afterwards: three caption buttons over one continuous surface.
- **"Not a working copy" for a working copy**, which is D31.

### D31 — One folder, two spellings: every request path is respelled before anything compares it

Windows spells most folders two ways — the 8.3 short form `%TEMP%` hands out (`ARUKO~1.SAK`) and
the long form a file dialog or Explorer hands out (`Aruko.Sakai`) — and the daemon compares
paths as strings. A session takes its root's spelling from whichever path opened it first, so the
other spelling of the same folder was a stranger:

- **Opened long, asked short:** status with a scope answered "not in the working copy" (D29's
  check), and so did every command that writes — `add`, `revert`, `commit` and the rest have
  compared targets against the root this way since D19, so this half predates D29.
- **Opened short, asked long:** the lookup missed and the daemon opened a *second* session — a
  second index and a second watcher — for the same folder, which passed every test by accident.

Found by launching the app, which read its recent list from a path written with `%TEMP%`. Pinned
first by four end-to-end tests against a real daemon, which take both spellings of a fixture's own
temp folder from Windows and fail both ways round without the fix.

**The fix is at the door, not at the comparisons.** `DaemonRequestHandler.HandleAsync` respells
every path of every request before dispatching it, so everything downstream — session lookup,
target checks, scope, the paths handed to `svn` — sees one spelling. The long form is the one,
because it is what wc.db, Explorer and file dialogs all use. Three pieces:

- `RequestSpelling` — pure; rewrites each request type's path fields and nothing else. A test
  enumerates every `DaemonRequest` in Protocol against the set it covers, so a request type added
  later cannot slip past unrespelled.
- `PathRespelling` — pure; respells through the longest existing ancestor, because a request
  routinely names a path that does not exist yet — a move's destination, a file deleted from disk.
- `LongPathSpelling` — the Win32 `GetLongPathNameW` call behind it, taken by the handler as the
  `RespellPath` delegate. A path Windows cannot respell is returned as given: the worst case is the
  two spellings staying apart, never a request failing because of respelling. Its two partial
  branches are the other-OS side of `OperatingSystem.IsWindows()` and that failure path.

*Status: implemented. All seven test binaries green (1626). The two pure types are at 100% line and
branch. Through the shipped binaries, the app given the short spelling of a fixture now lists it.*

### D32 — A ticked set is one request: the daemon marks what the disk says happened, then commits

GUI slice 3's commit needs no manual marking: ticking a `?` adds it, ticking a `!` deletes it, and a
D27 pair records a move. That is `CommitSelectionRequest(Paths, Message)`, not front-end glue over
`add` + `delete` + `move` + `commit`, so what decides a commit lives once, in the daemon, and
`sv commit` can adopt it later. `CommitSelectionPlanner` (pure) reads each ticked path against the
session's status; `SelectionCommitter` runs moves, then adds, then deletions, then one
`--depth empty` commit naming every ticked node.

Measured on 1.8.15 before it was built, on a fixture made for it:

- **Both halves of a recorded move must be in one commit** — naming either alone is `E200009`. So a
  ticked half without the other is refused up front rather than committed as a plain delete or add,
  which is exactly the history loss this exists to prevent.
- **`svn add dir` recurses, and `--depth empty` then commits the folder alone**, leaving its
  contents `A`. So after adding an unversioned directory the committer reads status again and names
  everything the add scheduled beneath it. Ignored files are skipped by the add and so never named.
  Recursive is the operator's call: a folder of new assets is the common case.
- **Deletions are recorded without `--force`** (`SvnRecordDeletionCommand`). A missing node is
  scheduled either way; a file that came back with edits is refused (`E195006`) and left alone,
  where `--force` would unlink it. That is what makes it safe to plan from the warm index instead
  of paying a second scan: the rename route is read off disk too, so no step trusts status alone.
- **A hook refusing the commit leaves `A` and `D` in place, and the retry commits.**

**Three answers, and the difference between the last two is the contract.** `CommitSelectionResponse`
is committed (or nothing to send). `SelectionNotCommittedResponse` means a step that writes ran
and then something failed: `FailedStep` says where, `Scheduled` lists what finished, `Failure` is
SVN's own text, and nothing is rolled back — rolling back would be a second write on a failure path.
`ErrorResponse` from this request always means nothing was written: a plan refusal (conflicted,
obstructed, ignored, external, incomplete, unknown, half a rename) or a rename route that refused
before any other write.

D20's directory rules moved out of `ChangePicker` into `Frontend.DecidedSubtrees`, so `sv pick` and
the GUI's tick list obey one copy. On the CLI fallback there are no D27 pairs, so a hand rename
there commits as delete and add — the same answer `sv st` already gives it on that reader.

*Status: implemented. All seven test binaries green (1934). Planner, committer and `DecidedSubtrees`
at 100% line and branch. Against real repositories: add, a folder with an ignored file inside it,
delete, rename with `copyfrom` read back from the log, a mixed set in one revision leaving an
unticked edit local, a pre-commit hook refusal followed by a retry, and a refused half-rename that
touched nothing. The GUI half is not built.*

**The CLI adopted it in two places, and plain `sv commit` was not one of them.** The operator
decided that `sv commit PATH` keeps SVN's meaning and marks nothing. It still sends a
`CommitRequest`, and a test pins that. Marking is opt-in:

- **`sv pick`** sends its picked set as a `CommitSelectionRequest`, and now offers `?` and `!`
  nodes as well. A D27 pair is one question that sends both halves, and the question says which
  mark a yes makes. A pair the named path cuts in two is not offered at all. `a` sweeps up edits,
  `!` and renames but leaves the remaining `?` local, which matches the GUI's unticked `?`. That
  part was not confirmed by the operator.
- **`sv commit --mark PATH...`** names every changed node under the paths one by one. Conflicted,
  obstructed and incomplete nodes are named on purpose, so the daemon refuses the lot the way
  plain `svn commit` would, instead of committing around them.

Measured on 1.8.15 for the picker: `svn status` lists every missing child of a missing directory,
and `svn delete` on the directory records the whole subtree. With the directory left alone, a
missing child deletes and commits on its own. So a *sent* missing directory settles its missing
subtree and a declined one settles nothing. `ChangePicker` holds that rule for now, because
`DecidedSubtrees` does not have it yet. It belongs there, since the GUI's tick list hits the same
case.

*Status: CLI half implemented, all seven binaries green (2128). Driven through the published `sv`
and daemon on throwaway copies of a fixture: plain `sv commit` sent the two edits and left every
`?` and `!` as it was. `--mark` recorded the rename (`copyfrom` in the log), the missing
directory's deletion and the added folder in one revision. Half a rename was refused with nothing
written. A pre-commit hook refusal printed the step, the marks and "nothing was rolled back",
exited 3, and the retry committed. The interactive `sv pick` walk itself was not driven, since
there is no terminal here. It is covered by `IPrompt` tests only.*

### D33 — svn writes paths in its console's code page, so the daemon gives it a UTF-8 console

`SvnCommand` decodes svn's output as UTF-8, and on Windows svn does not write UTF-8. The path text
it prints — diff `Index:`/`---`/`+++`/`Property changes on:` lines, status text, errors — is in the
*console's output code page*. `LC_ALL=C` changes the language, not this. File content lines in a
diff are the file's raw bytes and are never transcoded. Measured on 1.8.15, Windows 11, Polish
locale (OEM 852, ANSI 1250), for `zażółć.txt`:

| svn started with…                                   | `Index:` bytes for the name        | read as UTF-8 |
|-----------------------------------------------------|------------------------------------|---------------|
| a console at 852 (a terminal, or `CreateNoWindow`)   | `za be a2 88 86 .txt`              | `za����.txt`  |
| no console at all (`DETACHED_PROCESS`)               | `za bf f3 b3 e6 .txt` (1250)       | `za����.txt`  |
| a console at 65001                                   | `za c5 bc c3 b3 c5 82 c4 87 .txt`  | `zażółć.txt`  |

A name outside the code page — `ドラゴン.txt` — comes out as `????` in the first two rows, and that
is not recoverable by decoding differently. Only the third row is right for every name.

**The daemon owns a console and sets it to 65001 before anything starts svn** (`SvnConsole.UseUtf8`,
first line of `Program.cs`). Every `svn` is started with `CreateNoWindow` false, so it inherits that
console rather than getting one of its own. What the daemon finds depends on how it was started:

- **By `DaemonChannel` — `sv` and the app, i.e. always in production.** `CreateNoWindow` gives the
  daemon a fresh windowless console of its own (measured: one process attached, code page 852,
  no window), independent of the caller's. Switching it touches nobody else, so `sv` never changes
  the user's terminal — and the CLI never runs svn itself; only the daemon does.
- **With no console** (`DETACHED_PROCESS`). `AllocConsoleWithOptions` in its no-window mode, then
  the switch. Without this each svn would get a new console — at 852, and with a window. The call
  exists from Windows 11 24H2; on older Windows the process stays without one, as before.
- **In a terminal, run by hand.** The code page belongs to the console, not the process, so the
  switch is the terminal's too; the scope `UseUtf8` returns puts the old pages back on disposal.
  A daemon killed outright leaves the terminal at 65001.

`Utf8ConsoleRule` is the decision — Windows or not, console or none, already UTF-8 both ways or not
— as a pure function of those answers, so every side is tested on either OS. `SvnConsole` is the
Win32 half, exercised by the integration tests; the other-OS side of `IsWindows()` and the
pre-24H2 fallback are what no test on this machine reaches.

**Tests get the same guarantee from an assembly hook, not from `SvnCommand`.** `Svn.Tests` and
`Daemon.Tests` run svn in-process, so `SvnConsoleForTheRun` calls `UseUtf8` before the first test
and disposes it after the last: a developer's terminal is switched for the run and put back. The
constructor of `SvnCommand` was the other place it could live, and was rejected: a type that
quietly rewrites a console shared with the user's terminal on construction is a side effect nobody
reading the call site would expect, and the process that owns the console is the one to decide.
`LaunchedDaemonTests` starts the built daemon through `DaemonChannel`, which gives it its own
console at 852, so it fails if `Program.cs` stops making the switch.

**Open end: arguments.** Paths svn *reads* are not fixed by this. A Japanese name passed as an
argument is refused (`E200009`, "some targets don't exist") from a console at 65001 as well, so
this client build does not take its arguments through the console code page. Polish names, which
exist in both 852 and 1250, work either way. A request that names such a file by its own path —
diff, add, commit of that one file — is still broken on this build; `--targets` or naming the
parent are the two routes worth measuring before deciding.

*Status: implemented. All seven test binaries green (2078). The working-copy diff of `zażółć.txt`
and `ドラゴン.txt` — in-process, and through the built daemon started by `DaemonChannel` — failed
before and passes after. Run with no console at all (`DETACHED_PROCESS`), the Svn suite's name tests
pass through the windowless-console branch. The pre-24H2 fallback has not been run.*

### D34 — svn 1.14 answers differently from 1.8.15, and both are supported

Every measurement above D34 was taken on 1.8.15. CI installs what a person installing svn today
gets — 1.14 — and 26 tests went red on it. Each difference was measured against a real 1.14.5
(SlikSVN on Windows, apt's 1.14.3 on Ubuntu) beside 1.8.15, in scratch repositories, before any code
changed. Supporting both is a decision taken 2026-09-23; CI runs the suite against each.

- **Lock and unlock exit one when anything was refused** — D22's trap, fixed upstream. The warnings
  and stdout are unchanged, a mixed call still locks what it could, and one line is added after the
  warnings: `E200009: One or more locks could not be obtained` (or `released`). `SvnRefusalSummary`
  reads "at least one warning, and no error but E200009" as refusals, not failure. Hard failures
  (E155008 for a directory, E195013 from unlock's local check) are unchanged and still throw.
- **Resolve has two new success lines.** `Merge conflicts in '<p>' marked as resolved.` for text and
  `Tree conflict at '<p>' marked as resolved.` for tree conflicts; property conflicts keep 1.8's
  wording, and a node with a text *and* a property conflict is announced once for each, so
  `SvnResolveOutput` anchors each shape at both ends and collapses repeats. A tree conflict refused
  anything but `working` is W195024 rather than W155027; the final error is E155027 rather than
  E205011. Unlike 1.8, the kind of conflict settled is now in the text — the resolve bullet under
  PLAN.md's open questions is narrower on 1.14 than it reads.
- **Resolve says nothing about a missing target.** 1.8 refused it with W155010 and exited one; 1.14
  exits zero, silent. A mistyped `sv resolve` target reads as "nothing was conflicted". Open question.
- **Queued work stops writes only.** 1.14 still refuses every write with E155037, but `status`,
  `info`, `diff` and `log` go straight through and show nothing wrong. D26's reason the fallback's
  zero is honest — `svn status` fails first — holds on 1.8 alone. wc.db is the only reader that
  sees queued work on 1.14; the fallback only runs when wc.db cannot be read.
- **A file held open during resolve** warns W720005 (the move that failed) rather than W155009, and
  on 1.14 the conflict flag is already cleared while the copy is left needing cleanup.
- **Update of a path outside every working copy** fails with E155007 on 1.14 where 1.8 skipped it.
- **Off Windows, plain `LC_ALL=C` breaks svn outright** on any directory holding a non-ASCII name
  (E000022 converting the entry to UTF-8). `SvnLocale` sets C.UTF-8 on Linux and en_US.UTF-8 on
  macOS: English messages, a charset that can name every file. Measured on Ubuntu; macOS is CI's.
- **On Windows, svn writes its text in the ANSI code page — and that is the build, not 1.14.**
  SlikSVN 1.8.15 does it too, as do TortoiseSVN's and VisualSVN's 1.14.5; only the Win32SVN-style
  1.8.15 D33 was measured on follows the console. `zażółć.txt` arrives as CP1250 bytes here and
  best-fit CP1252 (`zazólc`) on a US runner; a name outside the code page is `????`. No locale
  changes it, and XML output is UTF-8 on every build. So `SvnOutputText` decodes each line as UTF-8
  when it is valid UTF-8 and in the ANSI code page otherwise — per line, because a diff's content is
  the file's own bytes — and a diff gets its true names back from `svn diff --summarize --xml`:
  `DiffHeaderRespelling` matches each header against what `WideCharToMultiByte` makes of every
  summarised name, the call svn's own conversion goes through, and leaves a header two names share
  as svn wrote it. One extra svn run per non-empty diff, on Windows only. Notification text — the
  paths `add`, `commit` and friends list — is still lossy for a name outside the code page; nothing
  acts on those paths, since a write drops the whole index (D19). D33's console switch stays for
  the builds that do follow it.
- **Arguments go through the ANSI code page too — on every Windows build.** Measured with
  `ドラゴン.txt` on a CP1250 machine: the Win32SVN-style and SlikSVN 1.8.15, SlikSVN and TortoiseSVN
  1.14.5 all refuse it named as an argument (`W155010`, the name arriving as `????`) and named in a
  `--targets` file, which is read in the code page as well. So on Windows a file whose name the
  code page cannot hold cannot be named to svn at all — only reached through a folder above it.
  D33's open end, closed with a worse answer than it hoped for; an open question in PLAN.md.

*Status: the Svn and Daemon suites green against the Win32SVN-style 1.8.15 and SlikSVN 1.14.5 on
Windows, and against apt's 1.14.3 in an Ubuntu container. macOS and the US code page are CI's.*

### D35 — Listing everything is one field that existed, and one that stops it being re-sent

The GUI's Changed / All toggle exists so an untouched file has a line to lock from. **The listing
needed no new field**: `StatusRequest.IncludeUnmodified` is what `sv st -v` already sends, and
both readers already hold every node — wc.db directly, and the fallback because `SvnStatusCommand`
always runs `svn status --verbose` (D14). The rule for "nothing to report" moved from the daemon's
`StatusFilter` to `Core.CleanNode`, because the App now needs it too to tell the changes in an All
listing from the rest; it is pure, which is the test for Core rather than Frontend (as with
`TargetCoverage`).

**What All costs was measured before anything was built on it.** Release daemon and a Release
harness over the real `WorkingCopyViewModel` and `DaemonWorkingCopyStatus`, no view, on
`subverted-100k/wc` (101,001 nodes, 29-byte files) with the index warm, so no file is read and the
file cache does not enter into it; five runs each, twice over, on a dev box:

| one request, warm | wall | inside the daemon |
|---|---|---|
| Changed (1 entry) | 11–38 ms | 7–29 ms |
| All (101,001 entries) | 250–830 ms | 7–43 ms |
| a poll through the view model, Changed | 9–16 ms | |
| a poll through the view model, All | 400–700 ms | |
| a poll, All, naming the held scan | 3–5 ms | |

Filtering is not the cost — the daemon is as quick either way. Serialising, moving and reading a
hundred thousand entries is, and the view model's diffing of them after. Once a second, that is
half of every second. **So a poll names the scan its listing came from** (`StatusRequest.HeldScan`,
from `StatusResponse.ScanId`), and while the daemon would answer from that same scan it sends
`StatusUnchangedResponse` instead. The id is a fresh `Guid` per reading, not the generation: an
unwatched session rescans at the same generation every time, and a restarted daemon counts from
zero. The front-end forgets it on any failure and on every toggle, since the id says nothing about
a different request.

**Compatibility follows the protocol's rule that absence means the old behaviour.** `HeldScan` and
`ScanId` are optional and default to null; a daemon that predates them ignores the one and never
sends the other, so the App sends null and always gets the entries. `StatusUnchangedResponse` is only
ever sent to a request that named a scan, so a front-end that never names one never sees it.

**What it does not fix.** A listing that did change is still the full ~400–700 ms, once per change.
An unwatched session (`WatcherState.Unavailable`) rescans for every request, so every answer is a
new scan and All pays the full price each poll. Under the fallback, `svn status` reports no node
kind, so All lists folders as lines and Lock — which needs a file — is offered on none of them.
Not measured: Avalonia realising the rows (the list is virtualised) and whether the entries are read
off the UI thread in the real app.

*Status: implemented. All seven test binaries green; the new presentation rules and `CleanNode` at
100% line and branch. The toggle is seen in headless view tests only, not in the real app.*

### D3 — The working copy is authoritative

Local history (M3) lives in a separate content-addressed store that is purely derived. It is never
the source of truth for what is committed. `git-svn` made the opposite choice and its SVN side
became a fragile export.

Consequence: the store can be deleted at any time without data loss beyond local checkpoints, and
plain `svn` keeps working in the same directory throughout.

*Status: not started.*

### D4 — IPC over Unix domain sockets

Windows 10+ supports `AF_UNIX`, so `Socket` with `UnixDomainSocketEndPoint` is one implementation
across Windows, macOS and Linux — rather than named pipes on Windows plus sockets elsewhere.

The wire format is a four-byte little-endian length prefix and a UTF-8 JSON body. JSON because a
protocol you can read with `nc` is worth more during M1 than the bytes a binary one would save;
source-generated because the front-end's whole budget is tens of milliseconds and reflective
serializer warm-up is a measurable slice of that. It is also what keeps `sv` AOT-compilable, which
turns out to be the only remaining lever on the number the M1 criterion measures.

Three things the tests settled rather than the design:

- **The type discriminator is `$kind`, not `kind`.** `ErrorResponse` carries a `Kind` of its own
  and `System.Text.Json` refuses a type whose property collides with the discriminator name. It
  fails at serialization time, not at build time.
- **A missing discriminator raises `NotSupportedException`, not `JsonException`.** Catching only
  the latter let a malformed message escape the protocol layer as something no caller expects.
- **A close between frames and a close mid-frame are different events.** The first is a front-end
  hanging up, which is how every conversation ends; the second is corruption. Collapsing them
  either turns normal disconnects into errors or swallows a truncated message.

The socket lives at `$SUBVERTED_SOCKET`, or a per-user path under `%LOCALAPPDATA%` /
`$XDG_RUNTIME_DIR` / the system temp directory. It is deliberately short: `AF_UNIX` caps the whole
path near 104 bytes on macOS.

*Status: implemented and verified against a real socket on Windows, including the daemon's
stale-socket recovery — a killed daemon leaves its file behind, and a file nothing answers on is
deleted rather than treated as "already running".*

### D5 — The filesystem watcher needs overflow handling

`FileSystemWatcher`'s internal buffer overflows when many changes arrive at once — precisely what
an asset import does. On overflow it raises `Error` and silently drops events. On Linux,
`fs.inotify.max_user_watches` defaults low enough that a game project exhausts it.

The design that came out of this is that **the session never trusts the watcher; it trusts a
counter.** Every event — created, changed, deleted, renamed, *and overflow* — does one thing:
increment a generation. A scan records the generation it started at, and the held result is
current only while that number still matches. Three consequences fall out of it:

- An overflow is not a special case. It raises the same signal as a single write, so events being
  dropped costs a rescan rather than a wrong answer, and the re-arm that follows it can fail
  without anything being lost.
- **A change that lands mid-scan cannot be swallowed by that scan**, because the generation is
  read *before* the tree walk, not after. The cost is one wasted rescan; the alternative is
  serving an answer from before someone's save.
- A watcher that is not watching at all makes every answer stale by definition, so the daemon
  rescans on every request. Slow and right rather than fast and wrong, and
  `sv daemon status` says `NOT WATCHED` so the slowness is explicable.

`IndexWarmer` then rescans stale sessions once they have been quiet for half a second, which is
what makes "warm" the common case rather than the lucky one — an import that writes for a minute
is left alone until it stops instead of being rescanned per file. The *rule* for that lives in
`RescanSchedule`, which holds no clock and does no I/O; the warmer is a timer and a loop with no
decisions left in it, which is the only way the settle boundary gets tested without a test that
waits half a second to find out.

For a long time the one thing this design did *not* do was update a single node incrementally: a
change anywhere cost a full rescan. D17 adds that, without giving up anything above — the counter
is still the only thing that decides whether the index stands, and the path is only a hint about
what to redo.

*Status: implemented, and verified end to end against a real working copy — a file written while
the daemon already held the tree warm shows up without anything being restarted.*

### D6 — Network operations shell out

Update, commit, lock, and log go through the `svn` binary until profiling says otherwise.
Reimplementing `ra_svn` or DAV is a large, high-risk project that buys nothing until the local
side is already fast.

`SvnCommand` is the one type that starts a process, and `SvnLogCommand` and `SvnDiffCommand` are
the only two that build an argument list — so the whole of "Subverted knows how to drive `svn`"
is three small files. The daemon takes the two reads as delegates (`ReadRevisionLog`,
`ReadWorkingCopyDiff`) rather than as SVN types, which is what lets `DaemonRequestHandler` be
tested without `svn` on PATH, a repository, or a server.

Five things the fixture settled, none of them guessed:

- **`LC_ALL=C` on the child process.** SVN translates its own output. On the development machine
  a diff header reads `(kopia robocza)`, not `(working copy)`, and every error message comes back
  in Polish. A front-end matching on prefixes would be guessing at which language the studio
  installed. One environment variable fixes it on all three platforms.
- **`--non-interactive`, always.** A daemon sitting on a password prompt answers nothing and says
  nothing about why.
- **The target is passed relative to the working-copy root.** SVN echoes the path it was given,
  so handing it an absolute path made every diff header read
  `Index: C:/Users/.../Temp/subverted-it-a311.../wc/src/a.txt`. `SvnTarget.Within` turns it back
  into `src/a.txt`, and `.` for the root. Separators are left alone: SVN normalises to forward
  slashes itself, and rewriting them would mangle a Unix filename containing a backslash.
- **A target containing `@` has to be terminated with another one, except for `diff`.** SVN reads
  what follows the last `@` as a revision, so `icon@2x.png` failed every other subcommand with
  `E200009`. `svn diff` alone takes the argument as a whole path, which is why it calls
  `SvnTarget.WithinForDiff` and is the only caller that does. See D24 in PLAN.md.
- **Log is parsed, diff is not.** `svn log --xml` goes through `SvnLogXml` into `RevisionEntry`,
  because a front-end wants the fields. `svn diff` is passed through as text — reproducing SVN's
  diff format, down to how it renders property changes and binary files, would only produce a
  second answer to disagree with.

`sv d` writes that text verbatim when its output is redirected, and splits it into coloured lines
only for a terminal. Printing the lines would re-terminate every content line with the console's
newline: `svn diff` writes its headers with CRLF on Windows and the file's own bytes for content,
so a line-by-line reprint produces a patch that no longer matches the file it came from. Verified
by byte-comparing `sv d` against `svn diff` and applying the result with `patch`.

*Status: implemented for `log` and `diff`, verified against SVN 1.8.15 on a real repository.
Update, commit and lock are not started. `svn` must be on PATH.*

## Testing shape

Logic that is hard to get right is extracted into pure, I/O-free types so it can be tested
exhaustively without a working copy. `NodeStatusResolver` is the worked example: the scanner does
the filesystem access and hands the resolver a `NodeSnapshot?`, so every status rule is a pure
function of two arguments.

This is the pattern to follow, not an accident. When something resists testing, the answer is
almost always to split the decision from the I/O rather than to reach for a mock.

M1 applied it to two things that look like they have to be integration-tested and do not:

- **`WorkingCopySession`** — the cache-and-rescan logic — takes an `IWorkingCopyScan` and an
  `IChangeNotifier`, both defined by the session rather than by their implementations. A fake scan
  that *counts how often it ran* is what proves the contract: a cache that never serves warm and a
  cache that serves stale both pass every test that only checks the returned entries.
- **`ConsolePager`** takes the "another screenful?" prompt as a function. Off-by-one in a pager is
  the difference between seeing a line and never seeing it, and that arithmetic should not need a
  terminal to check.

What the fakes cannot reach is covered once, properly, by `DaemonEndToEndTests`: a real working
copy built by driving `svn`, a real wc.db, a real `FileSystemWatcher` and a real socket. That is
where "a file written while the daemon was already warm still shows up" is actually established —
no fake notifier can establish it, because the question is whether the platform tells us at all.

Two things that only the real thing caught, both worth remembering:

- A hosted service is *started*, not *bound*. Nothing in `BackgroundService`'s contract says the
  listener is up when `StartAsync` returns, and a test that assumes it passes alone and fails
  under parallel load.
- `svn commit` leaves the working-copy root at the revision before it, so a later `propset` on
  `.` fails as out of date. The fixture updates after committing.
