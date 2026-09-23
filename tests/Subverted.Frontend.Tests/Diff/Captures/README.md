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

## Revision diffs (`rev-*.diff`)

What the History view parses: `svn diff --non-interactive --change N <root-url>/<path>@N`, run by
`capture-revision.cs` the way `SvnRevisionDiffCommand` runs it — from the working-copy root, each
path segment URL-escaped, `LC_ALL=C`. From `subverted-history`, built by `build-history.ps1`
(r1–r9, plus a `wc-behind` checkout left mixed at r3 with `art` at r5).

To rebuild: `build-history.ps1` (it refuses to overwrite an existing fixture), then
`dotnet run capture-revision.cs -- %TEMP%\subverted-history\wc <repo-path> <N> <out> ...`.

| Revision | Path | What `svn diff -c` printed |
| --- | --- | --- |
| r2 edit | `/a.txt` | an ordinary hunk; headers read `(revision 1)` / `(revision 2)` |
| r6 delete | `/docs/readme.txt` | all removed lines, although the file is gone from every checkout |
| r3 move, old name | `/a.txt` | all removed lines |
| r3 move, new name | `/b.txt` | **nothing, exit 0** — compared with its copy source, it is unchanged |
| r1 binary add | `/art/hero.png` | the binary notice, then a second section with `svn:mime-type`, as a local add |
| r1 directory | `/art` | its files' sections, **named relative to the directory** |
| r3 root | `/` | both sides of the move; `b.txt` as a whole-file add |
| `@` / `%` in names | escaped in the URL | the name as it is, unescaped |

Two things that differ from a local diff: a file's header is its bare name (paths are relative to
the URL asked about, not to the working copy), and `svn diff -c N` on a *working-copy* path that no
longer exists fails with `E155010` — which is why the request names a repository path. A working
copy target also reads a peg revision off `@`, unlike plain `svn diff`.
