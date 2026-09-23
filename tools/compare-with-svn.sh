#!/usr/bin/env bash
# Diffs the probe against `svn status --no-ignore` on a working copy: both status axes, and the
# fourth column's `+`, which went unchecked until D28 because this script never read it.
# Throwaway verification harness, like Subverted.Probe itself — not part of the build.
set -u
wc_path="$1"
probe="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/Subverted.Probe"
cd "$wc_path" || exit 1

# Only real status lines: a code in column 1 or 2. Skips svn's localised conflict summary.
svn status --no-ignore | grep -E '^[ACDIMRX?!~][ CM]|^ [CM]' | while IFS= read -r line; do
  text="${line:0:1}"
  prop="${line:1:1}"
  hist="${line:3:1}"
  path="$(echo "${line:8}" | tr '\\' '/' | sed -E 's/[[:space:]]+$//')"
  case "$text" in
    ' ') t=Unmodified ;;  'M') t=Modified ;;    'A') t=Added ;;
    'D') t=Deleted ;;     'R') t=Replaced ;;    '!') t=Missing ;;
    '?') t=Unversioned ;; 'I') t=Ignored ;;     'C') t=Conflicted ;;
    '~') t=Obstructed ;;  'X') t=External ;;
    *) t="text:$text" ;;
  esac
  case "$prop" in
    ' ') p=- ;; 'M') p=props ;; 'C') p=propconflict ;; *) p="prop:$prop" ;;
  esac
  case "$hist" in
    '+') h=copied ;; *) h=- ;;
  esac
  echo "$t	$p	$h	$path"
done | sort > /tmp/cmp-svn.txt

dotnet run -c Release --project "$probe" -v q --nologo -- . --all 2>&1 \
  | sed -nE 's/^  ([A-Za-z]+) +(props)? +(copied)? +(.+)$/\1\t\2\t\3\t\4/p' \
  | awk -F'\t' '$4 != "" && $4 !~ /^[0-9]+$/ {
      print $1 "\t" ($2 == "props" ? "props" : "-") "\t" ($3 == "copied" ? "copied" : "-") "\t" $4
    }' \
  | sed -E 's/[[:space:]]+$//' | sort > /tmp/cmp-probe.txt

echo "svn=$(wc -l < /tmp/cmp-svn.txt) probe=$(wc -l < /tmp/cmp-probe.txt)"
diff /tmp/cmp-svn.txt /tmp/cmp-probe.txt && echo "IDENTICAL"
