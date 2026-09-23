#!/usr/bin/env bash
# Re-captures every .diff beside this script, running svn the way the daemon does (capture.cs).
# usage: capture-all.sh [fixture-dir] [props-fixture-dir]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
fixture="${1:-$TEMP/subverted-diff}"
props="${2:-$TEMP/subverted-props}"

dotnet run "$here/capture.cs" -- "$fixture/wc" \
  . "$here/root.diff" \
  sub "$here/sub.diff" \
  sub/a.txt "$here/sub-a.diff" \
  missing.txt "$here/missing.diff" \
  moved.txt "$here/moved.diff" \
  movededit.txt "$here/movededit.diff" \
  moveme.txt "$here/moveme.diff" \
  addeddir "$here/addeddir.diff" \
  bin-add.png "$here/bin-add.diff" \
  empty.txt "$here/empty.diff" \
  empty-props.txt "$here/empty-props.diff" \
  propdir "$here/propdir.diff" \
  'icon@2x.txt' "$here/icon.diff" \
  'zażółć.txt' "$here/polish.diff" \
  mergedir "$here/mergedir.diff" \
  mergemod "$here/mergemod.diff" \
  bin-del.bin "$here/bin-del.diff" \
  bin-mod-props.bin "$here/bin-mod-props.diff" \
  del-props.txt "$here/del-props.diff" \
  replaced.txt "$here/replaced.diff" \
  mac-cr.txt "$here/mac-cr.diff" \
  trickyprop.txt "$here/trickyprop.diff" \
  'with space.txt' "$here/with-space.diff" \
  empty-del.txt "$here/empty-del.diff"

dotnet run "$here/capture.cs" -- "$props/wc" . "$here/props-fixture.diff"
