#!/usr/bin/env bash
# Runs every test project's own executable, which is the run CLAUDE.md says to trust over
# `dotnet test`. Keeps going past a red project so one run reports all of them.
set -u

configuration="${1:-Debug}"
failed=()

for project in tests/*/; do
  name="$(basename "$project")"
  executable="${project}bin/$configuration/net10.0/$name"
  [[ -f "$executable.exe" ]] && executable="$executable.exe"

  [[ -n "${GITHUB_ACTIONS:-}" ]] && echo "::group::$name"
  "$executable" || failed+=("$name")
  [[ -n "${GITHUB_ACTIONS:-}" ]] && echo "::endgroup::"
done

if (( ${#failed[@]} )); then
  echo "Failed: ${failed[*]}" >&2
  exit 1
fi
echo "All test projects passed."
