# Captured sides and `svn diff` output

What the in-process diff's equivalence tests compare against. Every file is raw bytes, as svn
wrote or read them; `.gitattributes` marks them `-text`.

- **Client:** `svn --version --quiet` → `1.8.15`, on Windows, so svn's own lines end in CRLF.
- **How:** `python capture.py` builds `%TEMP%\subverted-ctx-capture` (r1 adds every case, r2 edits
  each, then a local edit on top) and writes, per case:
  - `.base` — the pristine of BASE (r2), found through wc.db's checksum; `.working` — the file on disk;
    `.diff` — `svn diff <name>`, run from the working-copy root with `LC_ALL=C`.
  - `.r1`, `.r2` — `svn cat -r 1` and `-r 2` of `URL@2`; `.rev.diff` — `svn diff -c 2 URL@2`.

`tie-insertion.txt` and `tie-deletion.txt` each have two smallest diffs; they are the shapes where
breaking the search's ties the other way from `libsvn_diff/lcs.c` picked the other one.
