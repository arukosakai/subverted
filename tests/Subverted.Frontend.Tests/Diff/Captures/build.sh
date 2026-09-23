#!/usr/bin/env bash
# Builds the subverted-diff fixture: a base at r1, then one local change per shape svn diff prints.
# usage: build.sh [fixture-dir]   (default: $TEMP/subverted-diff; its repo/ and wc/ are replaced)
set -euo pipefail
export MSYS_NO_PATHCONV=1
root="${1:-$TEMP/subverted-diff}"
mkdir -p "$root"
root="$(cd "$root" && pwd)"
cd "$root"
rm -rf repo wc
svnadmin create repo
svn checkout -q "file:///$(cygpath -m "$root/repo")" wc
cd wc

lines() { for i in $(seq 1 "$1"); do printf 'line %d\n' "$i"; done; }

lines 30 > mod.txt
printf 'doomed\ncontent\n' > del.txt
printf 'will vanish\n' > missing.txt
printf '\x00\x01\x02binary\x00' > bin-mod.bin
printf 'no newline at end' > noeol-old.txt
printf 'ends with newline\n' > noeol-new.txt
printf 'first\nlast without' > noeol-both.txt
printf 'one\r\ntwo\r\nthree\r\n' > crlf-styled.txt
printf 'one\r\ntwo\r\nthree\r\n' > crlf-raw.txt
printf 'retina\n' > 'icon@2x.txt'
printf 'polish\n' > 'zażółć.txt'
printf 'moving\n' > moveme.txt
printf 'moving and editing\n' > moveedit.txt
printf 'props here\n' > propfile.txt
printf 'only props\n' > proponly.txt
printf 'text and props\n' > textprops.txt
printf 'one\rtwo\rthree\n' > mac-cr.txt
printf '\x00old binary\x00' > bin-mod-props.bin
printf '\x00going binary\x00' > bin-del.bin
printf 'deleted with props\n' > del-props.txt
printf 'tricky\n' > trickyprop.txt
printf 'replace me\n' > replaced.txt
: > empty-del.txt
printf 'spaced\n' > 'with space.txt'
mkdir propdir sub mergedir mergemod
printf 'a\n' > sub/a.txt
printf 'b\n' > sub/b.txt
printf 'c\n' > sub/c.txt
cat > headers.txt <<'EOF'
plain
Index: foo
===================================================================
--- x	(revision 1)
+++ y	(working copy)
@@ -1 +1 @@
Property changes on: z
___________________________________________________________________
Added: svn:eol-style
## -0,0 +1 ##
\ No newline at end of file
Cannot display: file marked as a binary type.
tail
EOF

svn add -q --force .
svn propset -q svn:mime-type application/octet-stream bin-mod.bin
svn propset -q svn:eol-style CRLF crlf-styled.txt
svn propset -q to-modify 'old value' propfile.txt
svn propset -q to-delete 'bye' propfile.txt
svn propset -q multi $'first\nsecond\nthird\n' propfile.txt
svn propset -q svn:ignore $'*.tmp\n' propdir
svn propset -q dir-delete 'x' propdir
svn propset -q svn:mime-type application/octet-stream bin-mod-props.bin bin-del.bin
svn propset -q keep 'k' del-props.txt
svn propset -q tricky $'Added: x\n## -1 +1 ##\n' trickyprop.txt
svn propset -q svn:mergeinfo $'/branches/a:2\n/branches/gone:3' mergemod
svn commit -q -m base
svn update -q

# Local changes.
lines 30 | sed -e 's/^line 3$/line three/' -e 's/^line 27$/line twenty-seven/' > mod.txt
svn rm -q del.txt
rm missing.txt
printf '\x00\x01\x02BINARY changed\x00' > bin-mod.bin
printf '\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR' > bin-add.png
printf 'fresh file\nsecond line\n' > added.txt
: > empty.txt
printf 'no newline at end\nnow with more\n' > noeol-old.txt
printf 'ends with newline\nbut not any more' > noeol-new.txt
printf 'first\nlast changed' > noeol-both.txt
printf 'one\r\nTWO\r\nthree\r\n' > crlf-styled.txt
printf 'one\r\nTWO\r\nthree\r\n' > crlf-raw.txt
printf 'retina edited\n' > 'icon@2x.txt'
printf 'polish edited\n' > 'zażółć.txt'
svn mv -q moveme.txt moved.txt
svn mv -q moveedit.txt movededit.txt
printf 'moving and editing\nedited after the move\n' > movededit.txt
svn propset -q to-modify 'new value' propfile.txt
svn propdel -q to-delete propfile.txt
svn propset -q to-add 'hello' propfile.txt
svn propset -q multi $'first\nSECOND\nthird\n' propfile.txt
svn propset -q only 'yes' proponly.txt
printf 'text and props changed\n' > textprops.txt
svn propset -q svn:mime-type text/plain textprops.txt
svn propset -q svn:ignore $'*.tmp\n*.bak\n' propdir
svn propdel -q dir-delete propdir
svn propset -q dir-add 'y' propdir
mkdir addeddir
printf 'inside added dir\n' > addeddir/inner.txt
printf 'unversioned\n' > unversioned.txt
printf 'a edited\n' > sub/a.txt
svn rm -q sub/b.txt
printf 'd\n' > sub/d.txt
cat > headers.txt <<'EOF'
plain
Index: foo
===================================================================
--- x	(revision 2)
+++ y	(working copy)
@@ -1 +1 @@
Property changes on: z
___________________________________________________________________
Added: svn:eol-style
## -0,0 +1 ##
\ No newline at end of file
Cannot display: file marked as a binary type.
Index: added inside
tail
EOF
printf 'one\rTWO\rthree\n' > mac-cr.txt
printf '\x00new binary\x00' > bin-mod-props.bin
svn propset -q extra 'e' bin-mod-props.bin
svn rm -q bin-del.bin del-props.txt empty-del.txt
svn propset -q tricky $'Added: x\n## -1 +1 ##\nIndex: y\n' trickyprop.txt
: > empty-props.txt
svn rm -q replaced.txt
printf 'a replacement\n' > replaced.txt
svn propset -q svn:mergeinfo '/branches/feature:2' mergedir
svn propset -q svn:mergeinfo $'/branches/a:2-4\n/branches/b:5' mergemod
svn add -q bin-add.png added.txt empty.txt empty-props.txt replaced.txt addeddir sub/d.txt
svn propset -q flag 'on' empty-props.txt
printf 'spaced out\n' > 'with space.txt'
svn propset -q root-prop 'r' .
svn status
