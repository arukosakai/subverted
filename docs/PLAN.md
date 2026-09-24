# Subverted — Project Plan

A Subversion client that does not feel like a punishment.

## The problem

SVN's daily friction is not its UI. It is the model:

- Almost every operation is a server round-trip.
- There is no local history, so there is no way to save work-in-progress without publishing it.
- There is no staging area, so commits are all-or-nothing per file.
- Status on a large working copy is slow because it stats the whole tree, cold, every time.

TortoiseSVN addresses the UI and leaves all four of those alone. The command line does not
address anything. A genuinely better client has to attack the model.

The target user is a small game studio: tens of thousands of binary assets, a mix of programmers
and artists, file locking rather than merging for anything that is not text. SVN is a reasonable
choice for that workload — partial checkout, cheap locking, no full-history clone — which is why
replacing it with git is not on the table.

## Non-goals

- **Not a git clone.** Borrowing git's local-history ergonomics is the point; reproducing its
  branching model on top of SVN is not.
- **Not a server.** We talk to whatever SVN server the studio already runs.
- **Not a protocol reimplementation.** Network operations shell out to `svn` until profiling
  proves that is the bottleneck.
- **Not a web app.** Desktop, three platforms, one codebase.

## Principles

1. **The SVN working copy stays authoritative.** Everything we add on top is derived and
   disposable. Delete our metadata and you lose local history, nothing else.
2. **Never break the escape hatch.** `svn` on the command line must keep working in the same
   folder, at all times. If our client dies, the studio keeps shipping.
3. **Fast paths are optimisations, never the only path.** Every direct read of SVN's private
   state has a documented fallback to the supported CLI.
4. **Two audiences, one core.** Artists need get-latest / lock / send-work. Programmers need
   history and branching. Same daemon, different surfaces.
5. **Surface ambiguity, don't guess.** Where SVN's behaviour is unclear, we ask rather than
   encode a guess that silently corrupts someone's working copy.

## Milestones

Each milestone has an exit criterion. We do not start the next one until the current one meets it.

### M0 — Foundations *(met, against a criterion that was rewritten — see below)*

Read SVN's working-copy metadata directly and prove it is both correct and fast.

- `Subverted.Core` — status model, two-axis since D9.
- `Subverted.Svn` — `WcDbReader` (direct `.svn/wc.db` reads, version-gated),
  `NodeStatusResolver`, `PropertyStatusResolver`, `PristineComparer`, `SvnPropertySkel`,
  `SvnGlob` and the ignore rules (D10), `WorkingCopyScanner`.
- `tools/Subverted.Probe` — timing and correctness harness.
- `tools/compare-with-svn.sh` — diffs the probe against `svn status --no-ignore` on both axes.

**Exit, as originally written:** probe output matches `svn status` node-for-node on a real studio
working copy, *and is meaningfully faster*. If it is not faster, the daemon premise is wrong and
M1 does not start.

**Exit, as it now stands:** probe output matches `svn status` on every node it reports, versioned
or not, and the cold scan is at the filesystem-stat floor for the tree it walks. Per-invocation
speed moves to M1, where a resident process can actually be measured.

The criterion was rewritten *after* it failed, which is the kind of move that deserves to be
recorded rather than quietly applied. The reasoning, and the grounds for accepting it, are under
"Why the speed criterion moved" below. Decision taken 2026-09-19. A later measurement on a
working copy in a more realistic state met the original wording anyway — that is recorded there
too, and does not retroactively make the rewrite unnecessary.

**Correctness: met, with two known divergences.** Verified by diffing the probe against
`svn status --no-ignore` line for line on six fixture working copies (SVN 1.8.15, format 31),
covering unmodified, modified, same-size-rewrite, added, copied, deleted, missing, replaced,
excluded, changelisted, locked, unversioned and ignored nodes, both axes of SVN's two columns
(` M`, `MM`, `M `, `!M`, ` C`, `A `, `A  +`, `?`, `I`), and the working-copy root itself. Three of
the six fixtures diff clean with zero differing lines; the other three differ only by the two
divergences below.

*Correction, D28: `A  +` was never verified — the harness read columns 1 and 2 only, so it checked
the `A` and not the `+`, and the fixture's one copy was a file. A copied **directory** was misread
throughout: everything inside it reported `A`, including a file deleted from disk. Fixed, with the
harness reading column 4 now; see ARCHITECTURE.md D28.*

There were two more, and both went the other way round — they *hid* a state rather than escalating
one. **A write-locked working copy reported as clean** (D25): `svn status` prints `L` in its third
column for a directory a crashed client left locked, `sv st` printed nothing, and every write to
that copy was about to fail with `E155004`. **A working copy mid-operation reported as clean**
(D26): a `WORK_QUEUE` row makes `svn status` fail outright with `E155037` and read nothing, while
Subverted's fast path answered from the tables it does read and never looked at the queue. Neither
is in the list below, because both are fixed rather than accepted — but they belong in this
section's history, because in each case the gap was a deliberate decision nobody had measured the
cost of. The two that remain are accepted, and both over-report or escalate — neither hides work
from a commit:

- **A property conflict** prints ` C` under `svn`, text column blank. We report the whole node
  `Conflicted`, because telling a property conflict from a text one means parsing
  `ACTUAL_NODE.conflict_data`, which we do not yet read.
- **A translated file whose recorded size or mtime no longer matches** resolves to
  `NeedsPristineCompare` rather than to a verdict. Its working bytes are not its pristine bytes,
  so hashing proves nothing, and answering it the way `svn` does means reimplementing SVN's
  eol/keyword translation. This is the documented CLI fallback of principle 3, not a gap in the
  fast path: on a fresh checkout such files settle on size and mtime like any other, and it is
  setting `svn:eol-style` — which blanks SVN's cache — that pushes them onto this path.

Ignore rules are reproduced rather than approximated, which took its own round of verification
against real `svn`; the rules and what surprised us are in ARCHITECTURE.md D10.

**Performance: measured twice, on two different working-copy states, with opposite results.**
Both on the same 20,201-node copy (SVN 1.8.15, warm cache, Release build, three runs each). The
state of the tree turns out to matter far more than anything in our code.

*State A — straight after checkout. Every recorded mtime still matches disk.*

| | time |
|---|---|
| `svn status` | ~270 ms |
| probe, one-shot wall clock | ~670 ms |
| ⤷ .NET process startup | ~220 ms |
| ⤷ SQLite open and first query | ~124 ms |
| ⤷ scan (SQL 68 ms + walk and resolve ~258 ms) | ~326 ms |

Here nobody hashes anything: both sides settle every node on size and mtime alone. We are ~2.5×
slower and that gap will not close, because .NET startup alone is comparable to `svn status`'s
entire runtime.

*State B — `svn:needs-lock` on all 20k nodes, every mtime moved. Content byte-identical.*

This is what a working copy looks like after real work: files opened and saved, a revert, a
branch switch, an asset re-export that rewrote identical bytes. The metadata fast path cannot
settle anything, so both sides must compare content.

| | time |
|---|---|
| `svn status` | ~9.6 s |
| probe, one-shot wall clock | ~2.25 s |
| ⤷ scan | ~2.15 s |

**~4.3× faster, cold, one-shot, .NET startup included** — and both arrive at the same answer
(20,200 Unmodified, 1 Missing; 20,201 and 1 plus an unversioned file once D9 and D10 added the
root and unversioned detection). So the original criterion *is* met, just not in the state it was
first measured in.

Two caveats, so this is not read as more than it is:

- Re-measured after D9 and D10 at ~2.3 s over three Release runs, unchanged within noise. The
  extra property comparison and the ignore pass cost nothing measurable: a whole working copy
  typically has a handful of `ACTUAL_NODE` rows, and the tree walk was already happening.
- These files average 43 bytes; the whole tree is 0.9 MB. The gap is per-file overhead, not
  throughput. On real multi-megabyte assets the ratio should compress — **now measured, and it
  does**: ~5.4× instead of ~10×, on a 1.59 GiB fixture of 256 KB – 4 MB files. See M1 below and
  ARCHITECTURE.md D15.
- State B only produces an answer at all because of D8. Before the property skel was parsed, all
  20k `svn:needs-lock` nodes returned `NeedsPristineCompare` in ~330 ms — fast, and useless, since
  a real client can only resolve that by shelling out to `svn status` and paying the 9.6 s anyway.

#### Why the speed criterion moved

State A is the one the original criterion was written against, and the useful reading of it is the
split. Our scan costs about what `svn status` costs in total (326 ms vs 270 ms), and a bare
`walk + stat` of the same tree costs 320 ms on this machine — so the scan is already at the
filesystem floor, not wasting time. What makes us lose is the fixed startup cost, which is exactly
what a resident daemon pays once instead of per invocation.

A one-shot probe cannot test the actual premise either way. The premise is that status should be
*already known* when you ask, not recomputed quickly. So the criterion was restated in terms a
daemon can satisfy, and the speed bar moved to M1. State B, measured later, would have satisfied
even the original wording — but it would have been the right call regardless, because passing on
the strength of whichever fixture state happened to flatter us is not evidence either.

What the measurements establish, and what the rewritten criterion keeps:

- the scan is at the floor for walking the tree, so there is no hidden inefficiency being excused;
- the SQL is 68 ms of the 326 ms, so wc.db is not the bottleneck and a daemon holding the index in
  memory removes almost all of the rest;
- when real work has to happen, we already beat the CLI at it by a wide margin.

What it leaves unproven, and what M1 now has to carry: that a warm query is fast, and that
incremental watcher updates keep it warm without drifting from disk. If M1 misses its exit
criterion, the premise is genuinely in question and this decision should be revisited rather than
rewritten a second time.

### M1 — Daemon and CLI *(in progress)*

A resident process that keeps status warm, and a CLI worth using.

- Generic Host worker holding the index in memory. **Done.**
- Filesystem watcher with overflow handling (see ARCHITECTURE.md, D5). **Done.**
- IPC over Unix domain sockets. **Done** (D4).
- `sv st`, `sv log`, `sv d` — coloured, fast, paged. **Done.** Plus `sv daemon status` /
  `sv daemon stop`.
- `sv add`, `sv commit`, `sv revert` — the commands that change a working copy. **Done** (D19).
  Until these, Subverted was a very fast way to *look* at a checkout and could not alter one.
  They take the same route as log and diff: the CLI speaks `Protocol`, `Subverted.Svn` runs the
  client, the daemon holds each as a delegate. `sv revert` lists what would be lost and asks
  first — `--yes` to mean it from a script, and a redirected stdin without it is refused rather
  than defaulted to yes. `sv commit PATH...` sends only what is under those paths, which is the
  mechanism M3's file-level staging will sit on.
- `sv pick` — **M3's file-level staging, pulled forward** (D20). It needed none of the object
  store: a picked set is a commit that stops at the nodes it names. See M3 below for what that
  leaves M3 owning.
  Since D32 it sends a `CommitSelectionRequest`, so a yes to a `?`, `!` or hand-rename adds,
  deletes or moves it on the way; `sv commit --mark` is the same thing, opt-in, for a whole path.
  Plain `sv commit` is unchanged (operator's call, 2026-09-23).
- `sv up` — the command the studio runs most, and the one that was missing (D21). Same route as the
  other three that change a working copy. It exists as its own decision rather than as a fourth
  bullet on D19 because **`svn update` exits zero on a conflict**: the exit code says the client
  ran, not that the merge worked, so the counts are parsed out of SVN's text and `sv up` exits 4
  when the update left something for a human. That is the one place Subverted deliberately does not
  mirror `svn`'s exit codes.
- `sv lock`, `sv unlock` — the third thing principle 4 lists for artists, after get-latest and
  send-work (D22). A studio of binary assets works by locking, not by merging, and until these the
  only lock Subverted could do anything with was the `K` it had been *printing* since M0. They exist
  as their own decision for the same reason `sv up` did, one notch worse: **`svn lock` exits zero
  after refusing every path it was given** — a warning on stderr, nothing on stdout — so
  `svn lock x && open x` hands an artist a file somebody else is painting. `sv lock` and `sv unlock`
  read the warnings and exit 4 when anything was refused. `--steal` and `--break` are separate words
  because you steal a lock by taking it and break one by releasing it.
- `sv resolve` — what `sv up` had no answer for (D23). Since D21 an update that conflicted exited 4
  and said a human was needed; there was then nothing in Subverted that human could use, and the
  only way on was `svn resolve`. It exists as its own decision because of two measured facts that
  are D22's shape again: **`svn resolve`'s default depth is `empty`**, so naming a directory settles
  nothing, prints nothing and exits zero, and **without `--accept` it waits on stdin forever** —
  not "reads EOF and gives up", still waiting when a timeout killed it, which in a daemon is a hung
  request. So `--recursive` and `--accept` go on every invocation, and fixing the depth is what lets
  `sv resolve` say `nothing was conflicted` and have it be true. `--theirs` and `--base` discard the
  local side and so ask first like `sv revert`; `--mine` and `--working` keep it and do not. There
  is no default version, because SVN's own default is to ask a human and the daemon has no terminal.
- `sv cleanup` — the one place the `svn` escape hatch was still the only route out (D25). D23 had
  found the state by accident: a resolve blocked by a file open in another application leaves the
  working copy wedged, and `svn status` then fails outright. It exists as its own decision for the
  reason `sv lock` and `sv resolve` did, one notch further along — **`svn cleanup` writes nothing to
  either stream on any outcome**, so unlike a refused lock there is not even a warning to read, and
  the report has to be read out of wc.db on both sides of the run instead. It also always runs at
  the working-copy root, because cleanup's two halves scope differently: a run aimed at a subtree
  drains the whole work queue but releases only the locks reaching into that subtree, so it can exit
  zero having left the copy locked. It asks first — `svn help cleanup` warns that breaking a lock a
  live client is using can corrupt a working copy beyond repair — and `sv st` learned the `L` column
  in the same decision, because that is how anyone finds out they need it. D26 finished that half:
  `sv st` now leads with a warning on the *other* wedged state, the one where `svn status` itself
  refuses to answer, since a copy nobody can see through is the worst one to report as clean.
- `sv rm`, `sv mv` — the two ways a working copy changes shape, and the last commands still missing
  from it (D27). Until these, deleting or renaming an asset meant dropping back to `svn`, which for a
  studio is the operation an asset tree gets most. `sv mv` exists as its own decision because
  **`svn move` cannot record a rename that already happened**: with its source gone from disk it
  prints `A dst`, exits 1, and leaves *both* paths missing. So the route is decided from the
  filesystem before the client runs, and the after-the-fact case walks the working copy back to a
  state SVN can record from — nothing is compared, so a file renamed *and* edited keeps the edit.
  `sv rm` asks first like `sv revert`, and splits what it is about to take in two: a versioned file
  comes back until the delete is committed, and **an unversioned one has no pristine, so
  `svn delete --force` unlinks it silently and exits zero.**
- **Subverted notices a rename nobody told it about, which SVN itself cannot.** `sv st` pairs a
  missing node with the unversioned file holding its content — by the SHA-1 wc.db already records,
  never by name, and only when the match is unambiguous on both sides. That is the half that makes
  the two commands above worth having: SVN sees an unrelated `!` and `?`, committing that way ends
  the file's history at its old name, and nothing anywhere reports it.
- The CLI fallback principle 3 has promised since M0 — a session served by `svn status --xml` when
  wc.db cannot be read. **Done** (D14). It is a second `IWorkingCopyScan`, chosen once at open, so
  nothing above the session knows which reader answered.

`log` and `diff` shell out to `svn` (D6), and the front-end may not do that itself — only
`Subverted.Svn` knows SVN exists. So they go the long way round: `SvnCommand` runs the binary,
`SvnLogCommand` and `SvnDiffCommand` shape the call, the daemon takes both as delegates
(`ReadRevisionLog`, `ReadWorkingCopyDiff`) so its dispatch is testable without `svn` on PATH, and
the CLI sees only `LogResponse` and `DiffResponse`. Two things that only running it settled:

- **SVN translates its own output.** Without a forced C locale the diff headers come back as
  `(kopia robocza)` on the machine this was written on, and every prefix the front-end matches on
  becomes a guess about which language the studio installed.
- **An absolute path in, absolute paths out.** Asked about `C:/Users/…/wc/src/a.txt`, `svn diff`
  puts that string in every `Index:` line. `SvnTarget.Within` makes the target relative to the
  root the command already runs in, so the output reads `Index: src/a.txt`.

*Unversioned and ignored detection was listed here and was actually delivered in M0 (D10); the
bullet has been struck rather than left to be ticked twice.*

**Exit:** warm `sv st` under 50 ms on a 100k-file working copy; cold under 2 s.

**Measured at 100k, on a working copy of that size, rather than extrapolated.** The fixture is
101,001 nodes — 100,000 files across 1,000 directories, plus the directories and the root — on
SVN 1.8.15, Release build, three runs of each figure. The tree's state matters more than anything
in our code, so both states M0 identified are reported, and so does the operating system's file
cache: the same State A scan measured 1.7–2.8 s cold and 0.9–1.0 s warm, on identical code.

*State A — fresh checkout. Every recorded mtime still matches disk.*

| | time |
|---|---|
| `svn status` | ~0.69 s |
| scan only | 0.92 – 1.00 s |
| `sv st` cold, daemon side | 0.99 – 1.09 s |
| `sv st` cold, what the user waits for | **1.57 – 1.70 s** |
| `sv st` warm, daemon side | **1.2 – 1.6 ms** |
| `sv st` warm, what the user waits for | **147 – 149 ms** |

*State B — every mtime moved, content byte-identical. What a working copy looks like after real
work, and the state in which nothing can be settled without hashing.*

| | time |
|---|---|
| `svn status` | ~40 s |
| scan only, before D13 | 14.4 – 17.0 s |
| scan only, after D13 | **3.5 – 4.2 s** |
| `sv st` cold, daemon side | 1.97 – 2.11 s |
| `sv st` cold, what the user waits for | **2.48 – 2.65 s** |
| `sv st` warm, daemon side | **0.6 – 0.7 ms** |

**So the criterion is met on three of its four cells, and both misses have a named cause.**

- **Warm, daemon side: met by a factor of thirty.** It held from 20k to 100k exactly as predicted,
  because a warm answer is a dictionary lookup and a filter regardless of how much is in the
  dictionary.
- **Warm, user-facing: missed, at ~148 ms against 50 ms.** Essentially none of it is Subverted's
  code — the daemon's share is one millisecond of it and the rest is the .NET runtime starting.
  That is D12, and D12 cannot be closed on this machine: the front-end compiles under NativeAOT
  with zero trim or AOT warnings and **still cannot be linked here, because there is no MSVC
  linker on the box.** The binary has never been produced, run or timed.
- **Cold: met in State A at 1.6–1.7 s, missed in State B at 2.5–2.7 s.**

D13 is what moved the second of those. The content compare was 14 of the scan's 16 seconds at
100k, one file open at a time on one thread, and nothing in it is shared; hashing the batch across
every core cut it ~4.1× in a same-binary, same-cache A/B. It is 4× and not 8× because the cost is
one `open` syscall per file rather than arithmetic.

Two things this does *not* settle, stated so the table above is not read as more than it is:

- **The scan was all-or-nothing. It is not any more (D17).** Editing a tracked file in the
  101,001-node fixture now costs **3.4 – 9.3 ms** on the daemon instead of ~983 ms, because only
  the paths that moved are re-resolved. The rule is narrow on purpose — every changed path must
  already be a node the held scan knows as versioned — so a new file, a rename, an external, an
  `svn` operation and a watcher overflow all still pay for a full scan. What that buys is that the
  cold number below stops being the price of a save; it is now the price of a structural change.
- **These files average 29 bytes and the whole tree is 2.9 MB.** As at 20k, this measures per-file
  overhead, not throughput — which is not the cost a studio of multi-megabyte assets actually
  pays. **That case is now measured too**, immediately below.

**Measured at asset sizes, because every figure above is per-file overhead.** A second fixture —
1,000 files of 256 KB – 4 MB, 1.59 GiB, incompressible — separates throughput from file count.
Three runs each; the full breakdown and the follow-on findings are ARCHITECTURE.md D15.

| | State A, mtimes match | State B, every mtime moved |
|---|---|---|
| `svn status` | 87 – 111 ms | 4.7 – 5.4 s |
| scan only | 174 – 204 ms | 0.86 – 0.97 s |
| `sv st` cold, what the user waits for | 0.82 – 1.06 s | 1.43 – 1.60 s |
| `sv st` warm, daemon side | **0.1 – 0.2 ms** | **0.1 – 0.2 ms** |

Both predictions this plan had recorded hold, and they point opposite ways. The ratio against
`svn status` compresses — ~10× at 100k tiny files, **~5.4×** here. The parallel compare holds up
*better*, scaling ~7.8× on 8 cores rather than D13's 4.1×, because at this size the loop is bound
on SHA-1 arithmetic rather than on one `open` per file.

Three things that only measuring settled, none of them what was expected:

- **It is not I/O.** Reading the tree without hashing runs at 6.6–6.9 GB/s against 2.0–2.2 GB/s
  for read-and-hash. SHA-256 on the same bytes is ~2× *faster* than SHA-1, which is the signature
  of a CPU that accelerates one and not the other. We cannot swap it; wc.db records a SHA-1.
- **Per core we are slower than `svn`.** Hashing this tree on one thread takes ~6.2 s against
  `svn status`'s ~5.0 s: whatever `svn` does per byte is cheaper than a SHA-1. The whole advantage
  at asset sizes is eight cores against one. *What* it does has since been read out of its source
  and confirmed: `compare_and_verify` byte-compares the working file against the pristine stream
  and, on 1.8.x, has no checksum path at all. A `memcmp` beating an unaccelerated SHA-1 is the
  whole of it.
- **Warm is unchanged at 0.1–0.2 ms in both states**, as at 20k and 100k. File size does not reach
  the warm path, which is the premise the daemon exists for.

Stated so the table is not read as more than it is: the tree fits in 61.6 GB of RAM, so every
figure above is **cached** throughput. Disk-cold is the number the open question about D7 turns on,
and it has since been taken by evicting the cache rather than by growing the tree —
`tools/measure-pristine-compare.cs`, ARCHITECTURE.md D18. It answers D7 the opposite way to the
warm figures: cold, hashing and the pristine compare land inside each other's ranges, because both
go disk-bound and the compare reads twice the bytes. The daemon's warm path reaches neither.

**M1 is not finished, but what is left is now one known thing rather than an unknown.** Every
command in its list is built and works against a real working copy. Three of the exit criterion's
four cells are met on a working copy of the size the criterion names. The fourth is the .NET
startup cost, whose fix is designed, compiles clean, and needs a toolchain this machine does not
have.

One user-facing bug found by running the thing rather than by testing it, worth recording because
no unit test could have: **`sv st | grep x` hung forever on the invocation that starts the
daemon.** Windows hands a child process every inheritable handle its parent holds, not only the
three it is given, so the daemon kept a copy of `sv`'s own stdout and the shell never saw
end-of-file on a command that had already exited. Redirecting the daemon's three handles does not
help — the copy is the problem. `StandardHandleInheritance.Disable` clears the flag before the
spawn, and both halves are needed.

### M2 — GUI shell *(in progress)*

Avalonia front-end over the same daemon. Update, commit, diff, log. Nothing clever yet.

- **The shell and live status. Done** (D30). `Subverted.exe` opens a working copy — any folder in
  one, scoped to that folder as `sv st PATH` is — remembers recent ones, and keeps the listing
  current once a second while the window is in front, stopping when it is not. A daemon that stops
  answering leaves the last listing on screen marked stale rather than blanking it. WinUI 3's look
  on Windows 11 (Mica, content in the title bar, the system accent), glass surfaces in dark and
  light. Built on `Subverted.Frontend`, which the CLI now shares. Seen running on Windows 11, and
  its own title-bar decorations drawn so the surface runs to the top edge with only the caption
  buttons over it.
- **Diff of local changes (GUI.md slice 1). Done.** Selecting a row shows its diff beside the
  list: debounced, the stale request cancelled, the selection kept across the once-a-second resync.
  `UnifiedDiffParser` is tested on 24 byte-exact `svn diff` 1.8.15 captures. Seen running on
  Windows 11 against `subverted-diff`, selected through UI Automation rather than by hand, and the
  50k-line virtualisation was measured headless only. A second save of an already-modified file
  now refetches: status entries carry the file's size and write time (`FileFingerprint`), filled
  from the stat the scan already made, and a row that differs by one tick is a new row. Tested
  through the daemon's watcher end to end, not yet seen in the app. The `svn status` fallback
  cannot fill it, so under the fallback, and against a daemon older than the field, the gap
  remains. Known gap: the root row diffs the whole working copy, since `DiffRequest` has no depth.
- **Changed / All on the Changes table** (GUI.md slice 5, D35). All lists every unmodified file
  too, so an untouched one can be locked from its line; the commit set and every count stay about
  changes. The status request gained an optional held scan so a 100k-file All listing is re-sent
  only when it changed: 3–5 ms a poll unchanged, ~400–700 ms when it did, measured warm on
  `subverted-100k`. Headless tests only; not seen in the real app.
- **Delete from a line's menu (GUI.md slice 3), the last verb of that slice.** It asks first, over
  a fresh listing of the target with clean and ignored nodes in it, and lists what is lost for
  good first. It asks again if that list changed by the time Confirm is pressed. What is offered
  was decided by running `svn delete --force` on 1.8.15 against every status a line can have.
  **An obstruction (`~`) wedges the working copy, and `svn cleanup` cannot undo that.** An external
  inside a folder is deleted with its edits, and SVN prints nothing for it. The rules are in
  `Frontend`. Driven through the real view model, adapter and daemon on a throwaway repository,
  and seen in the real app deleting an edited file. **`sv rm` does not use them yet, and disagrees with them in three
  places:** its preview calls an added file recoverable, which it is not, it says nothing about an
  external's contents, and it will send an obstruction.
- **Revert… and Delete… on the folder pane's menu** (GUI.md slice 3, forum #63), so a clean
  folder can be deleted. They use the same prompts and rules as the line's menu. The root is
  refused, and Revert is greyed above the opened folder, because the listing there cannot name
  everything it would reach. Headless tests only; not seen in the real app.
- **A bug pass over what was built (2026-09-24).** Reviewed across the Changes screen, the diff
  path and History/shell, then fixed test-first:
  - A slow open overtaken by a second one, or an activation answered after a deactivate or
    another open, left a status poll running that nothing could stop.
  - A daemon the system would not run threw `Win32Exception` past every handler, and the app
    crashed on the next deactivate. It now reads as unreachable, in `sv` too.
  - A null entry in the recent list failed every start, and a save another instance was holding
    failed the open.
  - A folder revert or Take theirs confirmed after the listing changed under the question sent a
    list the person never saw. It now asks again with the new list (operator's call, forum #59).
  - History kept its BASE marker from before an update or commit made in the app.
  - Split-view copy of two runs picked with a gap interleaved them.
  - A folder whose diff held one file lost that file's name, and Open in app opened the folder;
    a folder's own property section had a blank header.
  - History, theme and recent rows, and every diff line, read their record's `ToString()` to a
    screen reader.

  Seen in the real app through UI Automation: the History and diff-line names. The rest is
  covered by headless tests only. Not fixed: a safe resolve sent while a Take theirs question is
  open would drop that question, but the question's overlay covers the table, so it cannot be
  reached.
- Commit, log and update — the next slices, one at a time. Their order and the screens they
  build are in `docs/GUI.md`.
- **macOS's Liquid Glass is not built.** The styling speaks the same language, but the real material
  is `NSGlassEffectView` through native interop, which has to be built and run on the macOS runner.
  That needs this checkout to be a git repository with the `mac` remote, which it currently is not.

**Exit:** a studio member can do a full day's work without opening TortoiseSVN.

### M3 — Local history

The feature that actually answers "I miss git".

- Content-addressed local object store.
- `sv save` (checkpoint), `sv stash`, `sv undo`, local log.
- `sv push` computes the diff against last-known server state and commits it.
- ~~**File-level interactive staging**~~ — **done ahead of the rest of M3, as `sv pick`** (D20).
  `git add -p`'s interaction with one *file* per prompt: walk the changed nodes one at a time,
  y/n/a/d/q, commit what was picked. The thing people actually want most days — an artist has
  touched nine files and means to send three — and it turned out to need neither the object store
  nor changelists, because a picked set is just a commit that stops at the nodes it names.
- Hunk-level staging, implemented in our layer since SVN has no index.

**Exit:** a week of local checkpoints can be replayed onto the server as a clean revision
sequence, and blowing away the object store leaves the working copy intact.

File-level staging did not need the object store, and was pulled forward. The expectation recorded
here was that it would sit on SVN changelists; it does not, and the reason is worth keeping. A
changelist is a *stored* selection — a second place the truth about what is staged can live, and
one that survives the command that made it. The walk needs no such thing: it asks, collects a list
in memory, and hands that list to one commit. Changelists remain the right footing for a selection
that has to outlive the prompt, which is a GUI's problem and not the CLI's.

Hunk-level staging is the one that has no native footing and has to be built in our layer. It is
also the one the open question below is about, and nothing in D20 makes it easier.

### M4 — Studio features

Where we leave Tortoise behind.

- Locks as first-class UI: who holds what, request / release / steal, live.
- Asset previews and image diff (swipe, onion-skin).
- Sparse checkout with a real interface.
- Artist mode: three buttons.

**Exit:** artists stop asking programmers to resolve their working copies.

### M5 — Branch and merge ergonomics

`sv switch feature/x` that hides the directory-copy mechanics and tracks the relationship locally.

**Exit:** branching is not the thing people avoid.

## Open questions

These need a human decision and are deliberately not resolved in code:

- **On Windows a name outside the ANSI code page cannot be named to svn** (D34) — not as an
  argument, not through `--targets`. `sv add`, `commit`, `revert`, `lock` of such a file by its own
  path fail; its folder works. Whether to fall back to the folder with a narrower depth, or to say
  so plainly and stop, is a decision about what a command naming one file may touch.
- **A mistyped `sv resolve` target is silent on 1.14** (D34). The daemon could check each target
  against its index before asking svn, and refuse the ones it does not hold.
- **On macOS the CLI fallback names a root by its real path** — `/private/var/…` for a copy reached
  through `/var` — while requests keep the spelling they came in with. Unmeasured beyond the two
  fallback tests; it bites only under the fallback, and only through a symlink.

- **Telling a property conflict from a text one** needs `ACTUAL_NODE.conflict_data` parsed — it is
  a skel, like properties were before D8. Until then a property conflict reports the whole node
  `Conflicted` rather than ` C`. D23 shipped conflict resolution without needing it, which is worth
  recording because this entry predicted the opposite: `svn resolve` is told which version to keep
  and never asked which kind of conflict it is settling, and its own notification is identical for
  all three. What the column would buy is now a narrower thing, and it is the next bullet.
- **Whether `sv resolve --working` should read the files it settled.** Its warning to check for
  `<<<<<<<` fires on every `--working`, including a tree conflict, where there are no markers and
  cannot be — SVN's notification does not say which kind of conflict it settled, so nothing in the
  output can tell them apart. Two ways to narrow it: parse `conflict_data` per the bullet above, or
  have the daemon read the resolved files and name the ones that really still have markers. The
  second is more honest and costs a read per resolved node; neither is built, because a warning that
  over-fires is the safe direction and picking between them is a design decision, not a fix.
- **Whether `svn diff`'s peg-revision exception survives a newer client.** D24 measured it on
  1.8.15, the only client on this machine: `diff` takes the whole argument as a path while every
  other subcommand splits it at `@`, so `SvnTarget` has one method for each and `diff` is the lone
  caller of the second. If a 1.9+ or 1.14 client eats pegs in `diff` too, that method becomes the
  wrong answer and `An_at_sign_in_a_name_does_not_stop_its_local_change_being_diffed` is what goes
  red. Worth re-running the eight cases on whatever client the studio actually installs, which is a
  thing to check rather than a thing to guess at now.
- ~~**Whether to byte-compare against the pristine *in addition to* hashing (D7).**~~ **Closed 2026-09-23: hash only.**
  Cold, the compare's lead fell into the noise; hashing has to stay for 1.15's optional
  pristines regardless; and a second content path is a second thing to keep correct on the code
  that decides what gets committed. The measurements are ARCHITECTURE.md D15 and D18.
- **Lock presence.** Showing teammates' locks live needs either aggressive polling or a small
  sidecar service. The second is better UX and more infrastructure for the studio to run. D22 made
  this sharper rather than answering it: `sv lock` now tells you who holds a path, but only at the
  moment you try to take it and only because SVN's refusal says so. `sv st` still shows the `K` of
  locks *this* working copy holds and nothing about anybody else's, because that is `svn status -u`
  — a server round trip on the one command whose whole premise is that it does not make any.
- **Locking a folder.** SVN cannot: `lock` takes no `-R` and refuses a directory outright, so
  `sv lock art/` fails. The daemon holds the index and could expand it to every versioned file
  underneath, which is the operation a studio actually wants. It is not built, because two thousand
  locks nobody will remember to release is a policy with consequences and SVN offers no bulk release
  either. Worth deciding when the lock UI of M4 is designed, since that is where the release half
  has to live too.
- ~~**Whether `sv mv` should repair a rename inside an uncommitted copy (D28).**~~ **Decided
  2026-09-23: yes, by moving the person's file back to the source instead of reverting it**, in
  every case rather than only inside copies. Built and measured; see ARCHITECTURE.md D28.
- **Hunk staging semantics.** Writing staged content to the working copy, committing, then
  restoring the remainder is the only way to do partial commits on SVN. Crash recovery in the
  middle of that needs a design before it is written.
- **Mutation testing.** Stryker.NET does not yet support Microsoft.Testing.Platform, which TUnit
  requires. See CLAUDE.md for the interim policy.

## Status

M0 complete; M1 partly done and M2 begun, see their sections for exactly which parts. Solution builds clean with
zero warnings, **3230 tests green** across seven test projects, status output diffed against
`svn status --no-ignore` on eight fixture working copies — column 4 included, as of D28 — with only
the two divergences above.

**A copied directory reads the way `svn` reads it, and column 4 is modelled (D28).** Chasing the
`+` that `sv mv` leaves behind showed the resolver had been treating every op_depth > 0 row as an
add without looking at the disk. So everything inside a copied folder printed `A` — an edited file,
an untouched one, and one the artist had deleted, which a commit then quietly publishes anyway,
because copies are made on the server, and the next update restores. The rule underneath is that
op_depth is the depth of the path an operation was aimed at, and every line of what follows from it
was measured against `svn status` on a fixture built for it. `sv st` now prints `A  +`, `M  +`,
`D  +`, `R  +`, `!` and `   +` exactly where `svn` does: identical on 46 lines of 46.

**`sv st PATH...` lists what `svn status PATH...` lists (D29)** — each target and everything under
it, the union of several, and the current directory when given none; until then PATH only chose the
working copy. The scope is its own field on the request, because four destructive previews send
their first target as the path and filter for the rest themselves — scoped by that, `sv revert a b`
would have shown only `a` before discarding both.

**One folder answers to both of its Windows spellings (D31).** The 8.3 short form `%TEMP%` hands
out and the long form a file dialog hands out were two strangers to the daemon: status and every
command that writes refused the other spelling as "not in the working copy", and asking in the other
order opened a second session for the same folder. Found by launching the app, not by a test; every
request path is now respelled to the long form before anything compares it.

It also turned up one thing D27 was about to get wrong. A rename *inside an uncommitted copy* could
now be detected, and the repair could not make it — `svn revert` refuses a node inside a copy. So
the repair stopped reverting: it moves the person's file back to the old path and lets `svn move`
carry it, which works inside copies, for a copy's own root, and out of a folder that was deleted.

**A file renamed outside SVN is now recognised as one, and can be recorded as one (D27).** This is
the first thing Subverted does that `svn` cannot do at all, rather than doing it faster or saying it
more clearly. An artist renames `hero.png` in Explorer; SVN sees a missing node and an unrelated new
file, and a commit in that state records a delete and a fresh add — the history stops at the old
name, with no error anywhere to notice. `sv st` now pairs the two by the SHA-1 wc.db already records
and says so, and `sv mv OLD NEW` records the rename after the fact.

Measured end to end through the shipped binaries: `svn move art/hero.png art/protagonist.png` on a
hand-renamed file fails with `E155010: Path '…' is not a directory` and exits 1; `sv mv` on the same
pair reports that it recorded a rename already made; and `svn log` on the new name then shows
`A /art/protagonist.png (from /art/hero.png:1)` and reaches back to r1. The history is kept, which
is the whole claim.

Three things worth recording, all of them measured rather than assumed:

- **`svn move` half-applies when its source is gone.** It prints `A dst` on stdout, exits 1, and
  leaves *both* paths missing. So the route is decided from the filesystem before the client runs,
  never by reading its complaint — and three quite different refusals all report as that same
  `is not a directory`, which is why `sv mv` phrases them itself.
- **`svn delete --force` on an unversioned file unlinks it, prints nothing and exits zero.** There
  is no pristine behind it and nothing brings it back. `sv rm` previews its targets split on exactly
  that line, and its report counts the silent removals out loud.
- **A lock does not follow a rename.** `svn status` shows `D    K` on the old path afterwards: the
  file is unlocked under its new name and nothing says so. `sv mv` warns.

Detection is deliberately narrow. Content only, never names; and a digest two nodes share is dropped
from both sides, because which file became which is then a guess and a wrong one tells somebody
their asset moved somewhere it did not. A rename plus an edit breaks the match and is not claimed —
`sv mv` still records it when told to, which is the safe direction for a rule that would otherwise
have to guess.

**One bug this found by being run rather than tested:** `sv rm` on a *clean* versioned file said
"nothing to remove" and did nothing. The preview is built from a status listing and a file nobody has
edited is not in a default one, so the commonest removal there is was the one that silently failed.
The fix moved the choice of listing into `RemovalPreview.ListingFor`, where a test holds it.

This closed on `sv st` printing `A ` where `svn` printed `A  +`. D28 is what that turned into.

**`sv st` no longer answers for a working copy `svn` will not read without saying so (D26).** This
was the half D25 left open, and it is the more dangerous one: a `WC_LOCK` row leaves `svn status`
working, but a `WORK_QUEUE` row stops it dead at `E155037`, so the person can see *nothing* — and
Subverted answered the same tree cheerfully, because its fast path reads the tables it needs and
the work queue is not one of them. `sv st` now leads with a warning naming the count and pointing at
`sv cleanup`, and the listing follows. Measured end to end on a copy wedged the way a killed client
wedges one: `svn status` exits 1 having printed nothing, `sv st` prints the warning and then
`M art/hero.png`, `sv cleanup --yes` reports one operation finished, and both tools then agree the
copy is clean.

The warning goes *ahead* of the listing, unlike the notes `sv st` already prints for
`NeedsPristineCompare` and `Obstructed`. Those explain one letter on one line; this one says every
line under it is Subverted's reading alone, and a reader who has scrolled past it has missed it.

The exit code does not change. `sv st` describes a working copy rather than judging it — it already
exits zero on a tree full of conflicts — and `NeedsAttention` is for a command that *ran* and left
something behind. A script that wants to act on this has the count on the wire.

Two shape decisions worth recording, because both had a tempting wrong answer:

- **The count is read per scan, not once when the working copy is opened.** A client can crash
  mid-operation while the daemon is already holding that copy, and `sv cleanup` clears it while the
  daemon still holds it, so a value cached at open is wrong in both directions. It is re-read on the
  incremental path too — that path re-resolves a handful of paths and the queue is no part of what
  it re-resolves, so folding the count into the scan would go stale exactly where the daemon is
  fastest.
- **The CLI fallback reports zero, and that is not a claim the copy is healthy.** It cannot see the
  work queue at all; on that path a wedged copy makes `svn status` fail and the fallback surfaces
  the wedge as an error instead. So the capability is an optional interface the wc.db reader
  implements and the fallback does not, the same split `IIncrementalScan` already uses — rather than
  a member every reader has to answer dishonestly.

**A working copy a crashed client had wedged reported as clean, and now does not (D25).** `svn
status` prints `L` in its third column for a directory left write-locked; `sv st` printed nothing at
all, while every write to that copy was about to fail with `E155004: Run 'svn cleanup' to remove
locks`. Column 3 had been left blank deliberately and the code said so — what nobody had done was
measure what it cost. Both scanners answer it now, wc.db through `WC_LOCK` and the CLI fallback
through `svn status --xml`'s `wc-locked` attribute, so which one answered stays invisible.

**And `sv cleanup` is in, which is what that column now tells you to run.** It was the one place the
`svn` escape hatch was still the only route out. Its whole design is one measured fact: **`svn
cleanup` writes nothing to either stream, on any outcome** — no stdout, no stderr, exit zero,
whether it unwedged a working copy or found nothing wrong. That is `sv lock`'s problem one notch
worse, because a refused lock at least prints a warning. So the report is not parsed from the
client; it is read out of wc.db before and after the run, and the difference is the answer.

Three more things only running `svn` settled, all now tests:

- **There are two wedged states and they behave nothing like each other.** A `WC_LOCK` row leaves
  `svn status` working and printing `L`, and fails every *write* with `E155004`. A `WORK_QUEUE` row
  fails `svn status` itself, `E155037`, exit 1 — the person can see nothing at all until it clears.
- **The row a crashed client leaves is the root at every level**, `('', -1)`. Measured by parking an
  `svn commit` in a pre-commit hook that never returns and killing it. A client that fails
  *gracefully* leaves nothing — SVN unwinds its own lock on a clean error exit — so it has to die,
  which is exactly what `svn help cleanup` says and what made this reproducible in a test.
- **Cleanup's two halves scope differently**, which is why `sv cleanup` always runs at the root.
  `svn cleanup sub` drains the whole work queue — `WORK_QUEUE` has no path column — but releases
  only the locks reaching into `sub`, so a root lock at depth 0 survives it and the command exits
  zero with the copy still stuck. Third time SVN's own scoping has done less than the target implies,
  after `sv revert`'s `--depth infinity` and `sv resolve`'s `--recursive`.

`sv cleanup` asks first, alone among the commands that are not obviously destructive: `svn help
cleanup` warns that breaking a lock a *live* client is using can corrupt a working copy beyond
repair, and nothing can tell that lock from a dead client's. There is nothing to preview the way
`sv revert` previews, because the copy it is aimed at may be one `svn status` will not read.

**A filename with an `@` in it broke eight of the nine commands, and now does not (D24).** `svn`
splits every target at its last `@` and reads the rest as a revision, so `icon@2x.png` — the
standard name for a retina asset, which is to say a name a game studio's art folder is full of —
failed `add`, `commit`, `log`, `lock`, `unlock`, `revert`, `update` and `resolve` outright with
`E200009: a peg revision is not allowed here`. `sv st` listed the file perfectly the whole time,
because the fast path reads wc.db and never writes a target, which is exactly what made this
invisible. The fix is one terminator in `SvnTarget`, the single place a path becomes an argument;
the nine call sites did not change.

`svn diff` is the one subcommand that reads the whole argument as a path, so the escape breaks it
instead of fixing it — measured both ways on 1.8.15, and it is the reason `SvnTarget` now has a
second method with exactly one caller rather than a flag. The escape is appended only to targets
containing `@`, because `.@` is an error in its own right. Eight integration tests, one per command,
against a committed `art/icon@2x.png`: seven fail without the terminator and the diff one fails
*with* it, which is how both sides of that decision are held down.

**`sv resolve` is in, and with it the loop `sv up` opened in D21 finally closes (D23).** Since that
decision an update that conflicted exited 4 and told the artist a human was needed; there was then
nothing in Subverted for that human to use, and the only way on was to drop back to `svn`. Two
measured facts made it its own decision rather than a flag on `sv up`, and both are D22's shape
— a command that succeeds without doing anything:

- **`svn resolve`'s default depth is `empty`.** `svn resolve --accept working sub` prints nothing,
  resolves nothing and exits zero. `sv resolve` passes `--recursive` always, and fixing that is what
  earns it the right to say `nothing was conflicted` and have it be true.
- **Without `--accept` it never returns.** Not "reads end-of-file and gives up" — stdin *at*
  end-of-file, and it was still waiting when a 20-second timeout killed it. In the daemon that is a
  hung request holding a working copy, so `--accept` is always explicit and `--non-interactive`
  goes alongside it, which turns the prompt into a clean `E205000`.

Unlike a lock, a refused resolve does exit non-zero — but it is still **per-path**, the opposite of
`svn unlock`: a bad target does not stop the good ones being settled. So the warnings win over the
exit code, and `sv resolve` exits 4 when anything was refused. Four refusals measured, the last of
which is the one a studio hits by accident: a tree conflict asked for any version but the working
one (`W155027`), a missing node, a path in no working copy, and **a file still open in the
application that owns it** (`W155009`) — which also leaves the working copy wedged until
`svn cleanup` runs, something SVN's own wording says and a re-worded version would have lost.

**`--working` marks a node resolved with the merge markers still in it**, because SVN does not read
the file to check. `sv resolve --working` prints a line saying to check for `<<<<<<<` and saying
why; it cannot be narrower, because SVN's notification is word-for-word identical for a text, a
property and a tree conflict. `--theirs` and `--base` discard the local side and so ask first the
way `sv revert` does, refusing a redirected stdin rather than assuming yes; `--mine` and `--working`
keep it and do not ask. There is no default version, for the reason `sv lock` has no default target:
SVN's own default is to ask a human, and the daemon has no terminal to ask in.

**`sv lock` and `sv unlock` are in, and the studio's actual daily loop is now whole (D22):** get
latest, take the lock, work, send it, give the lock back. What made them their own decision is a
sharper version of the fact `sv up` turned on — **`svn lock` exits zero after refusing every path it
was given**, writing a warning to stderr and nothing at all to stdout. On a `.psd` the consequence
of believing that exit code is two people editing one file and one afternoon thrown away, so
Subverted reads the warnings and **exits 4 when anything was refused**, for `sv unlock` as well.

Three refusals were measured and all three behave that way: somebody else holds it (the only line in
the system that names *who*), the file has moved on since this copy last updated, and — from
`unlock` — the lock had already gone, which is how somebody learns theirs was stolen. SVN's wording
is passed through rather than re-worded, because it carries the holder's name and because a warning
code this build has never seen still prints as something a human can read.

Three more things only running `svn` settled, all now integration tests: a **directory** is an
outright failure and not a refusal (`lock` takes no `-R` and locks files only), which is why
`sv lock` has no default target at all; an **`unlock` naming one path this working copy does not
hold releases none of the others**, because the check happens before the server is asked; and
**stealing works without the loser finding out** — their working copy goes on printing `K` until
their own `svn update` prints `B` and clears it, and nothing on this side can reach that. Driven end
to end through the shipped `sv.exe` and daemon, including a lock a teammate really held.

`sv st` still says nothing about locks *other* people hold; that needs `svn status -u` and is the
open question below. A refused `sv lock` is the studio's only warning until it is answered.

**`sv up` is in, and its whole design is one measured fact (D21): `svn update` exits zero on a
conflict.** Text, property and tree conflicts all print a `Summary of conflicts:` block and all
exit 0, so `svn up && build` compiles a file full of `<<<<<<<`. Subverted reads the counts out of
SVN's own text and **exits 4 when the update left something for a human** — the one place it
deliberately does not mirror `svn`'s exit codes. The code describes *that update*: a second `sv up`
over an already-conflicted tree exits 0, because SVN counts what this run produced. `sv st` is what
answers whether the tree is clean.

Three more things only running `svn` settled, all now integration tests: "At revision N" and
"Updated to revision N" both report where the working copy stands, and a tree conflict prints the
first because it blocked everything; a path SVN will not touch is `Skipped` and also exit zero, so a
mistyped target looks like success — the daemon catches it first, making `sv up /some/typo` a named
error where `svn` is silently fine; and several targets are updated independently with a revision
each, which is why `sv up` takes at most one path. Driven end to end through the shipped `sv.exe`
and daemon, clean and conflicting, whole-tree and single-path.

**`sv pick` is M3's file-level staging, and it is in M1 (D20).** It walks the changed nodes one at
a time and commits what was said yes to. It needed nothing from M3 — no object store, and not the
changelists this plan had expected it to sit on — because a picked set is a commit that stops at
the nodes it names. That is one new value on the wire, `CommitScope`, and no second path that
decides what gets committed.

What only running `svn` settled, and what the picker's directory rules are built out of: a child
whose **added or replaced** parent is not in the same commit is refused outright (E200009); named
together they commit fine and the unpicked sibling stays local; and a deleted directory takes its
whole subtree even at `--depth empty`, while a deleted child named alone exits zero and sends
nothing. So a directory's answer settles its subtree, and the picker does not offer a choice that
is not one. All three are integration tests against a real repository.

Driven end to end through the shipped `sv.exe` and daemon: a picked set of two nodes committed as
r2 while the two declined ones stayed local, and `sv st` afterwards matched `svn status` on the
same tree. **The interaction itself has never been run** — this machine has no console to give a
child, so the prompt, the answers and the re-ask on a typo are tests rather than something a human
has watched. That is why the walk is split from the terminal at all; see D20.

**Subverted can change a working copy now (D19), not only read one.** `sv add`, `sv commit` and
`sv revert` go through the daemon like every other command, and the daemon drops its held index as
soon as the client has run — failure included — so `sv add x && sv st` shows the add rather than
waiting on a watcher event. Driven end to end against a real repository: add, a partial commit that
left the other path's changes local, and the revert gate refusing without a terminal and then
reverting exactly the listed nodes with `--yes`.

**The scan is incremental (D17).** Saving a tracked file in a 101,001-node working copy costs the
daemon **3.4 – 9.3 ms** instead of a ~983 ms rescan. D5's counter still decides whether the index
stands; the watcher now also reports *which* path moved, and that is used only to choose what to
redo. A change it cannot attribute — a buffer overflow — still rescans, and so does anything that
changes the set of nodes rather than one node's state. Verified by diffing the incremental answer
against a full rescan of the same tree, in a test and by hand through the daemon.

**Obstructed nodes and externals are modelled (D16), and both had been answering wrongly rather
than not answering.** A directory standing where a versioned file belongs reported *clean*; an
external reported `?`, which tells the user to add somebody else's working copy. The fallback,
which used to fail the whole read on either, now maps both — so a working copy containing an
external can use it at all. The new fixture diffs **identical** to `svn status --no-ignore`.

One user-visible consequence, called out because it changes something already on screen: **`~` now
means obstructed, the way SVN uses it, and the undecided state moved to `*`.** `StatusLine` exists
to be read side by side with `svn status`, and it had taken `~` only on the grounds that Subverted
did not model obstruction.

**The two readers now agree on every column they both report, revision included.** The fast path
was taking the revision off the highest `op_depth` row, where it means the copyfrom revision for a
copy and nothing at all for a delete; it now comes from the op_depth 0 row, which is what
`svn status -v` prints. Re-verified rather than patched, as this plan asked: the status-column diff
against `svn status --no-ignore` is unchanged on all six fixtures, and `sv st -v` was compared line
for line against `svn status -v` on the two fixtures carrying an add, a copy, a delete and a
replace — every revision matches. `Comparable` in the fallback agreement test now includes the
revision column it used to exclude, and reverting the one-line projection change turns five tests
red.

The CLI fallback principle 3 has always promised is now built (D14) — `sv st` keeps answering on a
working copy whose wc.db this build cannot parse. It was verified by narrowing the format gate so a
real format-31 working copy took the fallback, then running `sv st` through the real daemon and CLI:
identical output to the fast path on the property and ignore fixtures.

**Performance is now measured on the workload this tool exists for, not only on a fixture of
43-byte files.** `tools/make-asset-fixture.ps1` builds a 1.59 GiB working copy of 256 KB – 4 MB
binaries; the figures are in M1 above and the analysis in ARCHITECTURE.md D15. Nothing in the code
changed as a result. The headline is that the daemon's warm answer is unaffected by file size, the
cold advantage over `svn status` narrows from ~10× to ~5.4×, and the reason we still win is
parallelism rather than a better compare — per core, `svn` beats us.

**And it is now measured with the file cache dropped, which is where it stops flattering us.**
`tools/measure-pristine-compare.cs` evicts every working file and pristine before each pass, and
the eviction is verified by a read-only row that falls from ~13 GiB/s to ~3.5 GiB/s rather than
taken on trust. What it settles is the D7 question: the pristine compare's ~2.3× warm lead is
mostly the cache rather than the design, and cold the two overlap. D18. Nothing in the code changed
as a result of this one either.

Those 1280 are **615 + 153 + 87 + 425, from running each test project's own binary**, which is the
only run that currently works. `dotnet test` reported "zero tests ran" (exit 5) again on
2026-09-21, in a session that changed no C# whatsoever — so whatever it is, it is not the suite.
The second occurrence narrowed it: `--list-tests` returns zero too, so discovery is what comes back
empty, while the very same assembly is green both as `<Name>.exe` and as `dotnet <Name>.dll`. The
fault is in the SDK's MSBuild orchestration, not in the assemblies or in the test platform. See
CLAUDE.md; the cause is still unknown and the four binaries remain the run that settles it.

`sv st` has been run against a real fixture and its output compared line for line with
`svn status` on the same tree. It agrees node for node, with the two documented divergences and
one deliberate omission: column 4, the `+` that `svn` prints for a copy scheduled with history, is
one of the four status columns Subverted does not model and leaves blank rather than guess at.

`sv d` has been diffed against `svn diff` on the same tree and is **byte-identical** when
redirected, and the file it writes applies with `patch`. On a terminal it is deliberately not
byte-identical: the text is split into lines so it can be coloured and paged, which re-terminates
every line with the console's newline. `sv log` has been run against the same fixture and read
against `svn log -v`; it shows the same revisions, authors, times and changed paths, in a layout
closer to `git log` than to SVN's rows of dashes. Two differences are on purpose and neither
hides anything: blank lines inside a commit message are not printed, and the default listing stops
at the newest twenty and says so.

Coverage, measured rather than assumed:

- Every pure-logic type is at 100% line and branch — `NodeStatusResolver`,
  `PropertyStatusResolver`, `PristineComparer`, `SvnChecksum`, `SvnPropertySkel`, `SvnProperties`,
  `SvnGlob`, `DirectoryIgnorePatterns`, `WorkingCopyIgnoreRules`, `WcDbRow`,
  `WorkingCopyFileIndex` and — added with D14 — `SvnInfoXml`.
- `SvnStatusXml` is at 100% line and 100% branch on everything except line 85, the `item` switch,
  at 34/46. Every one of its arms is exercised, including all three unmodelled states; what is
  short is the hash buckets the compiler emits for a string switch, which is one of the two cases
  CLAUDE.md exempts and the same shortfall `CommandLine` has.
- `WorkingCopySessionFactory` is at 19/23 lines and 100% branch. The four uncovered lines are the
  `catch` that closes a scan when the watcher cannot be constructed at all. `FileSystemChangeNotifier`
  absorbs every failure it is designed for, so the only thing that reaches this is a platform with
  no `FileSystemWatcher` — the other-operating-system exemption. Without it an open wc.db would leak
  for the life of the daemon, so it stays.
- `WorkingCopyScanner` is at 100% too, via integration tests that script a throwaway repository
  through the real `svn` client. It was at 0% until those existed.
- `WcDbReader` is at 103/111 lines and 24/30 branches. What is left is corrupt-database handling:
  the SQLite failure path, a wc.db with no `wcroot` row, and a working copy with no repository
  row. Reaching those means writing a database Subversion would never write, which is the class of
  test this project has already been burned by, so they stay uncovered and stated rather than
  faked.
- `GlobalIgnoreConfiguration.Parse` is fully covered; locating and reading the config file is not,
  and neither is the Windows registry override, which is not implemented at all.

M1's pure logic is at 100% line and 100% branch on `StatusLine`, `StatusPalette`, `StatusReport`,
`StatusFilter`, `ConsolePager`, `DaemonInfoReport`, `TimingLine`, `ProtocolMessage`,
`RescanSchedule`, `WorkingCopySession`, `WorkingCopySessions`, and — added with `sv log` and
`sv d` — `SvnLogXml`, `SvnCommandResult`, `SvnTarget`, `LogReport`, `DiffReport`, `LogPalette` and
`DiffPalette`. Four types are short, each under one of the two exemptions CLAUDE.md allows, and
nothing else is:

- `ContainingRoot` 30/32 branches and `DaemonSocketPath` 10/20 — `OperatingSystem.IsWindows()`,
  one live side per machine. `ContainingRoot.Of` and `DaemonSocketPath.Resolve` take the platform's
  answer as an argument and are covered on both sides of it.
- `DaemonRequestHandler` 76/76 lines and 6/8 branches, and `CommandLine` 229/229 lines and 165/190
  branches — a discard arm the compiler insists on over a closed hierarchy nothing outside the
  assembly can extend, plus the hash buckets emitted for a string switch. Nothing else in either is
  short; the last real gap closed with D22, and it is named there.

Two branches that *were* short were deleted rather than excused, because neither was one of the
two allowed cases:

- `SvnLogXml` parsed with `XDocument`, whose `Root` is nullable although `Parse` never returns a
  document without one — so both the null check and the message that read it were unreachable.
  `XElement.Parse` returns the element itself and the case stops existing.
- `SvnCommand` used the static `Process.Start`, which is annotated as possibly returning null and
  only does so when it reuses a shell that `UseShellExecute = false` rules out. The instance
  `Start` has no null to check.

D20's types were measured the same way rather than assumed. `ChangePicker` 87/87 lines and 37/37
branches, `PickCandidates` 24/24 and 13/13, `PickConversation` 16/16 and 10/10, `PickReport` 21/21
and 2/2, `TargetCoverage` 8/8 and 8/8 — the last extracted out of `RevertPreview`, which is still
at 100% on both after losing it. Two fall short and both are named cases:

- `PickAnswers` is 15/15 lines and 46/62 branches — the hash buckets emitted for a string switch,
  the same shortfall `CommandLine` and `SvnStatusXml` have.
- `ConsolePrompt` is 0/3, and deliberately: it is three one-line calls to `Console`, and keeping
  them in a type with nothing else in it is the whole reason `PickConversation` can be tested.

D26 added no type with logic of its own — it is a capability interface, two one-line delegations and
one branch in each of the two places that matter — so it is measured on what it touched.
`WorkingCopySession` is 120/120 lines and 32/32 branches with the reader-cannot-look side of
`?? 0` taken as well as the other, and `StatusReport` is 74/74 and 44/44 with both sides of the
warning. `CurrentScan` is 10/10 and `SvnWorkingCopyScan` 20/20, both records and delegations.
`WorkingCopyScanner` is 356/362 and 98/100, unchanged by this: the shortfall is the pristine compare
on its incremental `Resolve`, which predates D26 and is a missing test rather than either allowed
case. `WorkingCopySessionFactory` is 48/56 and 4/4 — the uncovered lines are the wc.db-unreadable
fallback, which is D14's and not exercised from this project's tests.

D27's types were measured the same way. Everything it added is at 100% line and branch:
`UnrecordedMoveResolver` 21/21 and 14/14, `MoveRouteResolver` 8/8 and 8/8, `NodeMove` 19/19 and
10/10, `UnrecordedMoveRepair` 28/28 and 4/4 — the rollback included, reached by taking a write lock
so the revert inside it fails — `SvnMoveCommand` 14/14 and 2/2, `SvnDeleteCommand` 10/10 and 2/2
with both sides of the refusal, `MoveRefusal` 20/20 and 6/6 counting the throw, `MoveReport` 20/20
and 4/4, `RemovalReport` 11/11 and 4/4, `WorkingFileDigest` 11/11, and `StatusReport` 63/63 and
28/28 with both sides of the rename note. Two are short and both are named rather than excused:

- `RemovalPreview` is 27/27 lines and 17/18 branches. The one short is compiler-emitted, on the
  `AddRange` that ends the getter — a loop only reached when the list it enumerates is non-empty.
  Both list shapes above it are tested, one entry and two.
- `WorkingCopyScanner` is 240/243 lines and 84/86 branches. **Two of those three shortfalls predate
  D27**: lines 271–273 and the branch above them are the pristine compare on the incremental
  `Resolve`, which is the missing test already recorded under D26. The one D27 adds is a single side
  of one condition in `FindUnrecordedMoves`' guard — wc.db having recorded a checksum that is not a
  SHA-1. That is the corrupt-or-foreign-database class `WcDbReader` already leaves uncovered and
  stated, and reaching it means writing a database Subversion would not write.

One number in D26's entry below is reported differently here, because the method was wrong rather
than the code: `WorkingCopySession`'s outer class is 134/134 lines, but its `CurrentAsync` state
machine is 36/40, and the four are the double-check taken when another caller finished the scan
while this one waited on the semaphore. It is stable across runs, not flaky, and it is a missing
test rather than either allowed case — the existing concurrency tests reach the warm fast path
before the semaphore instead. D27 did not touch that block; it was simply not being counted.

D25's types are at 100% line and branch with nothing exempted, the throw included. `WriteLockCoverage`
is 15/15 lines and 12/12 branches, `CleanupEffect` 20/20 and 6/6, `CleanupOutcome` 6/6 and 4/4,
`CleanupReport` 28/28 and 16/16 counting the iterator the compiler emits for its summary, and
`SvnCleanupCommand`'s method body 12/12 and 2/2. `PendingCleanup`, `WorkingCopyWriteLock` and `CleanupCommand` are
records and a read. `StatusLine` is whole again at 29/29 and 26/26 with the third column on it.

That last one is the entry worth reading twice, because **it is the throw that D23 could not cover
and D24 took the one input away from.** A cleanup whose queued step names a file with no pristine to
install from — a node scheduled for addition and never committed — fails outright with `E155009`,
which is the client failing rather than refusing anything. It was found by running the shipped
binaries rather than by looking for it, and it also leaves a write lock of its own behind, so both
halves are tests now.

`CommandLine` is 294/294 lines with the same string-switch hash buckets short as before; the verb
switch grew by one arm and nothing else about it changed.

D23's types are at 100% line and branch except for two switch arms the type system rules out and
**one branch that is a missing test, said plainly here because it is neither of the two allowed
cases.** `SvnResolveOutput` is 26/26 with every branch taken, `SvnWarnings` 8/8 and 4/4 — it is
D22's `SvnLockOutput` renamed and generalised, because resolve reads the same `svn: warning:` lines
for a different reason and one parser with three commands' cases on it beats two copies.
`AffectedNodes` is 22/22, extracted out of `RevertPreview`, which is still 100% after losing it, and
`ConflictPreview` 12/12 and `ResolveCommand` 12/12 are the same shape. `ResolveOutcome`,
`ConflictResolution`, `ResolveRequest` and `ResolveResponse` are records and an enum.

- `ResolveReport` is 22/24 with one branch at 4/5: the discard arm of a switch over
  `ConflictResolution`, which is a closed enum. The allowed case.
- `SvnResolveCommand` is 30/32 with two. One is the same closed-enum discard arm. **The other is
  the throw, and it is a missing test.** It fires when `svn resolve` exits non-zero having printed
  no warning at all — the client failing rather than refusing a path — and every failure mode that
  could be produced on purpose came back as a *warning* instead: a tree conflict, a missing node, a
  path in no working copy, a file held open by another process, a read-only `wc.db`, a `wc.db` held
  open exclusively, a corrupt `wc.db`, a working copy in a format this client is too old to read,
  and a working copy left locked by a killed `svn`. The bare `E155004` with no warnings was seen
  once, in a copy whose subdirectory carried a second stale lock, and has not been reproduced since.
  The branch stays because the alternative — treating it as an outcome — would print
  `nothing was conflicted` over a command that never ran.

  D24 found the one input that reached it reproducibly and then took it away: a target `svn` parses
  as a peg revision exits 1 with `E200009` and no warning at all, which is precisely this branch —
  and it was Subverted sending a filename it should have escaped. Fixing the bug is the right
  outcome and leaves the branch uncovered; a test that resolved an `icon@2x.png` by *not* escaping
  it would be asserting that the bug is still there.

D22's types are at 100% line and branch with nothing exempted: `SvnLockCommand` 16/16 and 2/2
including the throw, and `LockReport` 17/17 with every branch taken. Its warning parser is now
`SvnWarnings`, measured above.
`ForeignLock`, `LockOutcome`, `LockRequest`, `UnlockRequest`, `LockResponse`, `UnlockResponse`,
`LockCommand` and `UnlockCommand` are records and an enum.

Measuring them turned up one gap that was nothing to do with locking and is now closed: **`sv revert
--some-option` had no test**, so the branch that stops an option being read as a path was uncovered
in the one command where doing so destroys work. Every other command had that test. `CommandLine` is
whole again at 229/229 lines, with only the verb switch's hash buckets short.

The CLI's `Program.ResolveAsync` and `Program.ConfirmResolveAsync` are uncovered like the rest of
`Program`, and were exercised by hand through the shipped binaries instead — including the refusal
to assume yes on a redirected stdin, which is the branch that matters there.

The CLI's `Program.LockAsync` and `Program.UnlockAsync` are uncovered like the rest of `Program`,
and were exercised by hand through the shipped binaries instead.

D21's types are at 100% line and branch with nothing exempted: `SvnUpdateOutput`, `UpdateReport`,
and `SvnUpdateCommand` including the throw — an update that cannot reach the repository is the one
way the client really fails, and it is an integration test against a repository moved out from under
a working copy. `UpdateOutcome`, `UpdateRequest`, `UpdateResponse` and `UpdateCommand` are records.
The CLI's `Program.UpdateAsync` is uncovered like the rest of `Program`, and was exercised by hand
through the shipped binaries instead.

`SvnCommand`, `SvnLogCommand` and `SvnDiffCommand` are I/O and are covered by integration tests
against a real repository — including the case where the client is not installed at all, which is
the one a studio machine actually hits.

`ContainingRoot` earned its last two covered branches the hard way: the test written to reach them
found that a working copy rooted at `/` matched nothing, because a guard added "defensively"
against an empty prefix was the thing breaking it. Deleting the guard was the fix.

The I/O types — `DaemonSocketServer`, `FileSystemChangeNotifier`, `IndexWarmer`, `DaemonChannel`,
`FileLogger` and the CLI's `Program` — are covered by `DaemonEndToEndTests` where they can be and
are honestly uncovered where they cannot. `DaemonChannel`'s daemon-starting path and the pager's
key handling have been exercised by hand, not by a test, and that is the difference between "run"
and "verified".

One thing that reads as verified and is not: **`sv d`'s coloured output has never been looked at.**
Its terminal branch is taken only when stdout is *not* redirected, and every run made so far was
captured — which is the verbatim branch by construction. `DiffReport` and `DiffPalette` are at
100% line and branch from tests, and no human has seen a green `+` come out of `sv d`.

The integration tests need `svn` on PATH and skip themselves with a reason when it is absent. On a
machine without it the suite still passes, which is worth knowing before reading a green run as
proof of everything above.

Settled by measurement rather than assumption:

- **`svn lock` and `svn unlock` exit zero after refusing every path they were given.** The refusal
  is a `svn: warning: Wnnnnnn:` line on stderr and there is nothing on stdout at all. Three were
  measured: W160035 (somebody else holds it, and the line names them), W160042 (`Lock failed: newer
  version of '/x' exists` — the file moved on since this copy last updated) and, from `unlock`,
  W160040 (`No lock on path '/x'` — it had already gone). See D22.
- **A directory is an outright failure, not a refusal.** `E155008: … is not a file`, exit 1, and
  `svn lock` does not accept `-R` — it locks files and only files.
- **`svn unlock` validates locally before asking the server, all-or-nothing.** One path this working
  copy does not hold fails the whole command with `E195013` and releases none of the others.
- **A stolen lock leaves the loser's working copy printing `K`.** `svn lock --force` succeeds
  silently; the other copy only finds out on its own `svn update`, which prints `B` for the broken
  lock and clears the stale token.
- **`svn resolve`'s default depth is `empty`, and without `--accept` it never returns.** A directory
  target resolves nothing, prints nothing and exits zero; and `svn resolve PATH` with no `--accept`
  was still waiting on stdin — already at end-of-file — when a 20-second timeout killed it.
  `--non-interactive` turns that second one into `E205000: invalid 'accept' ARG`. Killing it
  mid-wait leaves the working copy locked and needing `svn cleanup`. See D23.
- **Resolve is per-path and reports refusals in its exit code as well as in warnings**, which is the
  opposite of `svn unlock` on both counts: a bad target does not stop the good ones being resolved,
  and the exit is 1 rather than 0. Four refusals measured — `W155027` (a tree conflict takes only
  the working version), `W155010` (no such node), `W155007` (not a working copy) and `W155009`
  (**the file is open in another process**, which also wedges the working copy until `svn cleanup`
  runs — `svn status` itself then fails with `E155037`).
- **`--accept working` marks a node resolved with the conflict markers still in the file.** SVN does
  not read the file to check. Its "Resolved conflicted state of 'x'" is also word-for-word identical
  for a text, a property and a tree conflict, so nothing in the output says which kind was settled.
- **`svn update` exits zero on a conflict**, text, property and tree alike, printing a
  `Summary of conflicts:` block on stdout. It also exits zero on a `Skipped` path, including a
  target that is in no working copy at all. The exit code means the client ran; everything about
  whether the merge worked is in the text. See D21.
- **A tree conflict prints "At revision N", not "Updated to revision N"** — the same wording as an
  update with nothing to do, because the conflict blocked everything. Both report the revision the
  working copy now stands at.
- **Several update targets are updated independently**, each with its own `Updating 'x':` block and
  revision, summarised under `Summary of updates:`. There is no single revision for a multi-target
  update, which is why `sv up` takes one path.

- **`svn cleanup` on 1.8.15 takes no options but `--diff3-cmd`.** `--remove-unversioned`,
  `--remove-ignored` and `--vacuum-pristines` are all 1.9+, so the "cleanup can delete your
  unversioned files" hazard does not exist on this client and `sv cleanup` has no flag for it.
  Worth re-checking on whatever the studio installs, because on a newer client those are exactly the
  options that would need a confirmation of their own. Cleanup also takes **directories only** — a
  file target is `E155007: … is not a working copy directory`, the mirror of `svn lock` taking files
  only — and a path that does not exist is `E000002`.
- **`locked_levels` in `WC_LOCK` counts levels *below* the locked directory.** Nothing documents it.
  A lock on `sub` at `1` made `svn status` print `L` on `sub` and `sub/deep` and not on
  `sub/deep/deeper`; `-1` is every level and `0` is that directory alone. One row therefore lights
  up many directories, which is why the rule is a function rather than a path match. `L` is printed
  against **directories only** — the column wc.db keeps is `local_dir_relpath` — and deciding it
  from the path alone marks every file in a wedged working copy as locked. See D25.
- wc.db on SVN 1.8.15 uses `journal_mode = delete`, not WAL, so a read-only open works and the
  copy-then-read fallback is not needed. Re-check if the studio moves to 1.10+.
- The `svn` client translates its own output, including the `(working copy)` and `(revision N)`
  markers in a diff header. `LC_ALL=C` on the child process is what makes the output the same on
  a Polish install as on an English one; there is a test that fails without it.
- `svn` echoes the target path it was given, so an absolute path in means absolute paths in every
  `Index:` line out. The target is made relative to the root the command runs in.
- **`svn` splits every target at its last `@` and reads what follows as a revision.** `icon@2x.png`
  — what a retina asset is called, and an art folder is full of them — failed `add`, `commit`,
  `log`, `lock`, `unlock`, `revert`, `update` and `resolve` outright with
  `E200009: a peg revision is not allowed here`, and `log` and `info` with
  `E205000: Syntax error parsing peg revision '2x.png'`. A trailing `@` is SVN's own escape, is
  stripped before the path is used, and does not appear in the path SVN echoes back. **`svn diff`
  is the exception**: on 1.8.15 it reads the whole argument as a path, so a bare `icon@2x.png`
  diffs and `icon@2x.png@` is reported unversioned — measured both ways, which is why `SvnTarget`
  has a second method for that one caller and nothing else. The escape is appended only to targets
  that contain `@`, because `.@` is an error in its own right (`E125001`). See D24.
- `svn diff` writes its *headers* with the platform newline and its *content* lines with the
  file's own bytes. Reprinting the text line by line therefore changes the patch; `sv d` writes
  it verbatim when redirected for that reason.
- **An external has no `NODES` row.** It is registered only in the `EXTERNALS` table
  (`local_relpath`, `kind`) and is a separate working copy with its own `.svn`. Anything that
  enumerates NODES and calls the remainder unversioned will tell the user to add it.
- **`svn status --xml` lists an external twice** — the placeholder, then the external's own
  checkout and everything in it, because status recurses into it. `svn status -v` prints the
  placeholder's revision columns *blank*.
- **`NODES.revision` means two different things depending on `op_depth`.** At 0 it is the BASE
  revision — the one `svn status -v` prints. Above 0 it is the *copyfrom* revision, and it is NULL
  for a plain add and for the `base-deleted` layer of a delete. So the working revision has to be
  read off the op_depth 0 row specifically; taking it from the projected row printed a copy's
  source revision where `svn` prints `-`, and printed nothing for a delete and a replace where
  `svn` prints the revision being deleted. Verified against `svn status -v` on `subverted-fx1` and
  `subverted-props`, which between them carry an add, a copy, a delete and a replace.
- `NODES.checksum` is `$sha1$<40 lowercase hex>`; pristines live at
  `.svn/pristine/<first 2 chars>/<sha1>.svn-base`, uncompressed on 1.8 — and still uncompressed on
  trunk, which is read from Subversion's source rather than measured: `svn_wc__db_pristine_read`
  opens the file and wraps it with no decompression layer. What 1.15 changes is not the encoding
  but whether the file is there at all (`--store-pristine=no`).
- **`svn` decides "modified" by byte-comparing, not by hashing** — `compare_and_verify` in
  `libsvn_wc/questions.c` streams the working file against the pristine through
  `svn_stream_contents_same2`, with a size shortcut when no translation is needed. On 1.8.x there
  is no checksum path in it at all; trunk added one only for when the pristine is absent. This and
  the entry above it were read out of Subversion's source rather than measured here — weaker
  evidence than everything else in this list, and flagged so it is not quoted as if it were equal.
- SVN invalidates its recorded size and mtime by writing `-1` and `0`, not NULL — setting
  `svn:eol-style` does it. Reading `-1` as a real size reported untouched files as modified; see
  D11.
- `svn:ignore` and `svn:global-ignores` are both newline-delimited, and only the runtime config's
  `global-ignores` is whitespace-delimited. Ignore globs are case-sensitive even on Windows. See
  D10 for the rest, all of it checked against `svn` rather than against the documentation, which
  is wrong about the delimiter.
- Properties are a skel: a flat list of alternating name and value atoms, each written bare when
  it starts with a letter and holds no whitespace or parenthesis, and as
  `<length><one space><bytes>` otherwise. Read from a fixture carrying `svn:needs-lock`,
  `svn:eol-style`, `svn:ignore`, an empty value and a value containing spaces and parentheses;
  the blobs are in `SvnPropertySkelTests` verbatim.
- The working properties of a node are `ACTUAL_NODE.properties` when that row exists with a
  non-NULL column, and `NODES.properties` otherwise — `NODES` alone is the *pristine* set. Reading
  `NODES` made a locally deleted `svn:needs-lock` still look set, and a locally added
  `svn:eol-style` look absent, which is the dangerous direction: it would have let a translated
  file through the checksum compare.
