# Captured `svn diff` output

What `UnifiedDiffParser`'s tests parse. Every `.diff` here is real client output, byte for byte —
CRLF on SVN's own lines, whatever the file had on content lines — and `.gitattributes` marks them
`-text` so nothing normalises them.

- **Client:** `svn --version --quiet` → `1.8.15` (the CollabNet build in `C:\Program Files (x86)\Subversion`).
- **How:** `capture.cs` runs `svn diff --non-interactive <target>` the way `SvnCommand` and
  `SvnDiffCommand` do — from the working-copy root, with the target made root-relative by
  `Path.GetRelativePath` (so `sub\a.txt` on Windows), `LC_ALL=C`, and stdout decoded as UTF-8 —
  then writes the decoded string back out as UTF-8.
- **From:** `subverted-diff`, built by `build.sh` (base at r1, then one local change per case), and
  `props-fixture.diff` from the existing `subverted-props` fixture, read only.

To rebuild: `build.sh` (replaces `%TEMP%\subverted-diff\repo` and `wc`), then `capture-all.sh`.

## What SVN printed for the odd cases

| Local state | Target | What `svn diff` printed |
| --- | --- | --- |
| `!` missing (deleted from disk) | `missing.txt` | nothing, exit 0 |
| `?` unversioned | `unversioned.txt` | nothing on stdout, **exit 1**, `E150000 … is not under version control` — so no capture |
| `A +` moved, unedited | `moved.txt` | nothing, exit 0 |
| `D` source of that move | `moveme.txt` | the whole file as removed lines |
| `A +` moved, then edited | `movededit.txt` | only the edit, against the move's source (`--- movededit.txt (revision 1)`) |
| `A` added directory | `addeddir` | a section per file inside it; none for the directory |
| `A` binary added | `bin-add.png` | binary notice, then a *second* `Index:` section with the `svn:mime-type` it was given |
| `M` binary + property | `bin-mod-props.bin` | the same two-section shape |
| `D` binary | `bin-del.bin` | binary notice only |
| `A` empty file | `empty.txt` | just `Index:` and the `=====` line |
| `A` empty file with a property | `empty-props.txt` | `Index:`, `=====`, then the property section — no `---`/`+++` |
| `D` empty file | `empty-del.txt` | nothing, exit 0 |
| `D` file with properties | `del-props.txt` | the content removal only; no property section |
| ` M` property on a directory | `propdir` | a section named for the directory, properties only |
| ` M` property on the root | `.` | `Index: .`, printed **after** every file under it |
| `svn:mergeinfo` added / modified | `mergedir`, `mergemod` | indented `Merged …` / `Reverse-merged …` lines instead of a `##` hunk |
| lone-CR line endings | `mac-cr.txt` | content lines ending in bare `\r`, which SVN counts as lines |

Paths come back `/`-separated even when the target was given with `\`, relative to the root, and
the non-ASCII name arrives as UTF-8.
