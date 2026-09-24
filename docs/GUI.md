# Subverted — GUI plan

What M2 builds on top of the D30 shell, and in what order. `docs/PLAN.md` holds the milestone and
its exit criterion — *a studio member can do a full day's work without opening TortoiseSVN* — and
this file holds the shape of the screens that get there.

## What was borrowed, and from whom

Four clients were looked at: Axis and GitKraken for git, Versions and SmartSVN for SVN.

| Client | Taken | Left behind |
|---|---|---|
| Axis | History list on top, changed files and diff split beneath it; unified/split toggle and a context dropdown on the diff; a commit-details card | A toolbar of git verbs |
| GitKraken | Pending work pinned as the first row above history | The graph — SVN history is linear and a branch is a directory |
| Versions | Per-row action pills, an All / Changed toggle, incoming counts per working copy in the sidebar | Tree-first as the default; it buries the list of what changed |
| SmartSVN | "2,166 files hidden" beside a filter — say what is filtered out; an output log for writes | The density |

One studio developer uses SmartSVN's free edition and has exactly one complaint: **added and deleted
files have to be marked by hand unless you buy Pro.** The commit flow below is built around not
having that step.

## Layout

**Look: oto** (the operator's own Avalonia player): flat solid surfaces told apart by 1px lines,
small radii, dense 28px rows, a 64px icon rail, Lucide icons, Inter, and oto's theme presets
switchable from the rail. The accent is kept for the one primary action, Commit. **Layout:
SmartSVN**, in that look. This replaced the first shell's glass cards on 2026-09-23.

```
┌────┬──────────────────────────────────────────────────────────────────────┐
│    │ 📁 game ▾   https://svn/game                                  ● Live │
│ ☰  ├──────────────┬───────────────────────────────────────────────────────┤
│ 🕘 │ FOLDERS      │ 🔍 Filter by path   2 changes hidden · Show all   Open  Revert… │
│    │ ▾ game    9  │ State      Name              Folder                   │
│    │   ▸ art   3  │ ☑ ▍Modified hero.png         art                      │
│    │   ▸ src   2  │ ☑ ▍Missing  old.cs           src                      │
│    │              ├───────────────────────────────────────────────────────┤
│    │              │ ± DIFF ▍hero.png art Modified                         │
│    │              │ @@ -12,6 +12,7 @@                                     │
│    │              │  12 12   foo();                                       │
│ 📁 ├──────────────┴────────────────────────┬──────────────────────────────┤
│ 🎨 │ COMMIT                                │ OUTPUT                 Clear │
│    │ Commit message…                       │ 14:02:11 Commit 3 paths · …  │
│    │ Ctrl+Enter          [Commit 3 files]  │ Committed r1825              │
│    ├───────────────────────────────────────┴──────────────────────────────┤
│    │ C:\studio\game                     ● 3 modified  ● 1 missing  ● 1 added │
└────┴──────────────────────────────────────────────────────────────────────┘
```

- **Rail:** Changes and History, then Open and Theme at the foot.
- **Directory tree** on by default: every folder holding a change, with its count. Choosing one
  narrows the table exactly as the filter does, so what it hides is neither counted nor sent.
- **File table** top right, flat, with its diff beneath at full width.
- **Bottom strip:** the commit box beside an output log that keeps every commit and revert of the
  session with SVN's own text. A SmartSVN-style popup review window is an acceptable alternative the
  operator named; it is not built.
- **History** uses the same frame — the revisions table on top with the pinned "Local changes"
  line, and the picked revision's paths beside its diff beneath. The bottom strip is Changes' only.

The list's Flat/Tree toggle from slice 2 is still in `WorkingCopyViewModel` but no longer on
screen: the directory tree is the tree now.

## Slices

One at a time, each shippable, in this order.

### 1. Diff of local changes

- Selecting a row sends `DiffRequest` for that path. Debounced, the previous request cancelled, and
  fetched again when the status poll shows that row changed. Diffs are never served from memory
  (ARCHITECTURE, status request flow), and this does not change that.
- `UnifiedDiffParser` turns SVN's text into `DiffDocument → FileDiff → Hunk → DiffLine`. It lives in
  `Subverted.Frontend`: pure, and only front-ends need it. Its tests are built from `svn diff` output
  captured off the fixtures, not hand-written text.
- Three cases are first-class rather than parse failures: **binary** (a card with size and "open in
  app"; image diff is M4), **property changes** (a section of their own), and **added, deleted and
  missing** files.
- A virtualised line list of our own, not AvaloniaEdit — which carries the same Avalonia 11-vs-12
  risk the icon package did, and brings an editor where gutters and tones are all that is needed.

### 2. The condensed list

- Flat by default, Tree as a toggle. A filter box, and a line counting what is hidden.
- Conflicted and missing rows pinned to the top, as `ChangeSummary` already orders them.
- ↑/↓ moves the selection and the diff follows it; Space ticks; Enter opens the file.
- Context menu: reveal in Explorer / Finder, copy path, history of this file.

*Status: built.* Pinned rows stay above the tree as flat lines rather than inside their folders
(operator's call). Lines are per-path slots updated in place, so a resync or a tick never replaces
the container under the keyboard focus. "History of this file" raises `HistoryRequested` and is
disabled until slice 4 listens. `explorer /select,` opened the folder without selecting on
Windows 11 in every quoting, so Windows reveals through `SHOpenFolderAndSelectItems`. Seen in the
real app on `subverted-copy`: pinned rows, Tree, the diff following selection, the filter's hidden
count. Not seen there: Space, Enter and the context menu (headless tests only; the Windows reveal
was run directly), and anything on macOS. Tree has no collapse, and the filter re-lays out the
whole list per keystroke, unmeasured on a large listing.

### 3. Commit, add, revert — with no manual marking

- A tick box per row, a message box beneath the list, "Commit N files" and Ctrl+Enter.
- **Ticking a `?` means add and commit it; ticking a `!` means delete and commit it.** A `!` and `?`
  that D27 pairs by content show as one rename row, and ticking it records a move — history kept,
  where a hand-marked delete and add would end it at the old name.
- **Ticked by default:** edits, `!` and renames. **Unticked:** `?`, because unversioned usually means
  build output, and pre-ticking it is how junk reaches the repository.
- Scheduling and committing are **one daemon request**, not front-end glue, so what decides the
  commit lives in one place and `sv commit` can take the same behaviour later.
- **A commit that fails after scheduling leaves the schedule in place.** Nothing is hidden — the rows
  read `A` and `D` — and a retry simply works; rolling back would be a second write on a failure path.
- The ticked set commits as `CommitScope.ExactlyTheseNodes`, `sv pick`'s mechanism. D20's directory
  rules currently live in `Cli`'s `ChangePicker`; they move to `Frontend` so both front-ends obey one
  copy.
- Tick state is keyed by path beside the rows, so the once-a-second resync never clears it.
- Revert and delete confirm with the exact list of what is lost (D19, D27).

*Status: commit and revert built; delete not.* Rename rows come from `UnrecordedMoves`, one row
at the new path that sends both halves. A path takes its default tick once, when first listed
(`TickedPaths`), so an untick survives the resync. **Only ticks the filter shows are sent**
(operator's call); hidden ones are kept. D20 goes through `DecidedSubtrees`; a line a folder
decides shows an indeterminate, disabled box. The rule that a sent missing folder carries its
missing subtree (measured, forum #40) lives in `TickedSelection` for now and belongs in
`DecidedSubtrees`. The composer (`CommitComposerView`) and the notice are self-contained for the
bottom strip, and each attempt is raised as data (`CommitAttempt`, `RevertAttempt`). Revert is on a
line's menu with an overlay listing every path it reaches. **Measured on 1.8.15 while building it:
reverting a copy (`A +`) deletes it from disk, edits and unversioned contents included, and
reverting an obstruction deletes whatever is in its place.** The lines say so; a plain add stays.
Driven through the real view models, adapters and daemon on a throwaway repo: the default set
committed at r2 with the move's `copyfrom` in the log, a hook refusal left `A` and the retry
committed, an obstruction was refused with nothing written, and two reverts ran. Seen in the real
app: default ticks, the rename row, the button count. Not seen there: clicking, Ctrl+Enter, the
notice or the revert overlay (headless tests and renders only), and nothing on macOS.

### 4. History

- `LogResponse` already carries changed paths and copy sources; the list and paths pane need
  nothing new.
- Protocol: a `StartRevision` on `LogRequest` for paging as the list scrolls, and a new
  `RevisionDiffRequest(Path, Revision)` over `svn diff -c N` so an old revision's file opens in the
  same viewer.
- Search filters what is loaded. A marker shows where this working copy's BASE sits.

### 5. Update, conflicts, locks

- Update through `UpdateRequest`, with a result sheet; when `UpdateOutcome` says a person is needed,
  the conflicted rows light up.
- Resolve per row — mine / theirs / working — with D23's rule that discarding a side asks first.
- Lock and unlock from the toolbar and context menu. A refusal shows SVN's own text, which names
  the holder (D22).
- **Incoming count per working copy, on a slow timer** — only while the window is in front, like the
  status poll, and far less often, because every tick is a server round trip. A new request, since
  no warm index can answer what is on the server.

### 6. Diff polish — after History, not part of the M2 exit

- Split view, intraline highlighting on paired `-`/`+` lines as a pure function, and the context
  dropdown.

*Status: split view built; intraline highlighting and the context dropdown not.* Split is the
default, with a Split / Unified toggle over the lines (operator: "the diff should be two-pane").
`SplitLines` pairs a hunk's lines: context sits on both sides, and each run of changes between
context pairs its removals with its additions in order, padding the shorter side with blank filler.
`DiffLayout.Split` / `.Unified` choose how lines become rows; headers, binary cards, the no-lines row
and property sections are laid out the same by both, and property values are paired too. Each half
has its own number gutter; hunk headers span both. Column names read BASE / Working copy in Changes
and "Before rN" / rN in History, which shares the same `DiffLinesView`. Side by side, the list does
not scroll sideways — each half is half the viewport and a long line is cut off with an ellipsis;
Unified still scrolls. Copying from the split list gives the lines in unified order (a run's old
lines, then its new ones), so the same selection copies the same text in either layout. Seen only
in headless renders (dark and the Default Light preset), not in the real app, and nothing on macOS.

## Not in M2

The graph; stash, checkpoints and hunk staging (M3); image diff and other people's locks (M4);
switching branches (M5).

## Unverified

Each of these is an assumption the plan leans on and has not been run:

- **Whole-file context through `svn diff -x -U<N>`.** Slice 6's "show whole file" would come free if
  1.8.15 accepts it. Test it on a fixture before designing around it.
- **What query the incoming count uses.** `svn log -r BASE:HEAD` includes BASE itself, and a
  mixed-revision working copy has no single BASE. Settle both against a real checkout before slice 5
  picks one.
- **The timer's interval.** Minutes, not seconds; the number should come from what one check costs
  against the studio's server rather than be picked here.
