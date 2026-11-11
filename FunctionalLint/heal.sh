#!/usr/bin/env bash
# Build, auto-fix what FunctionalLint can fix safely, then report what's left and how to avoid it next time.
#
#   ./FunctionalLint/heal.sh path/to/Core.csproj
#
# Exit code 0 means the project builds clean. Anything else means there's work left, listed at the end.
set -uo pipefail

project="${1:?usage: heal.sh <project.csproj | solution>}"

# Rules with a code fix, and the habit that avoids them. (A function, not an associative array, for macOS's bash 3.)
habit() {
  case "$1" in
    FP0003) echo "Declare fields 'readonly', properties '{ get; init; }', and 'readonly record struct'." ;;
    FP0004) echo "Write records for data and static classes for functions." ;;
    FP0008) echo "Take and return IReadOnlyList / IReadOnlyDictionary / ImmutableArray, never List or IList." ;;
    FP0010) echo "Make async methods return Task, never void." ;;
  esac
}
fixable=(FP0003 FP0004 FP0008 FP0010)

# Prints one "file(line,col): error ID: message" per problem, without duplicates.
errors() {
  dotnet build "$project" -nologo -clp:NoSummary 2>&1 \
    | grep -E ': error [A-Z]+[0-9]+:' \
    | sed -E 's/ \[[^]]*\]$//' \
    | sort -u
  return 0
}

count() { grep -c ": error $1:" <<<"$2" || true; }

before="$(errors)"

if [[ -z "$before" ]]; then
  echo "Clean: no errors."
  exit 0
fi

# dotnet format reuses diagnostic positions across rules in one run, so fix one rule at a time.
fixed_any=false
for id in "${fixable[@]}"; do
  if [[ "$(count "$id" "$before")" -gt 0 ]]; then
    dotnet format analyzers "$project" --diagnostics "$id" --severity error --no-restore >/dev/null 2>&1
    fixed_any=true
  fi
done

if $fixed_any; then after="$(errors)"; else after="$before"; fi

echo "== Auto-fixed"
any=false
for id in "${fixable[@]}"; do
  n=$(( $(count "$id" "$before") - $(count "$id" "$after") ))
  if [[ $n -gt 0 ]]; then
    any=true
    echo "  $id x$n. Next time: $(habit "$id")"
  fi
done
$any || echo "  nothing"

echo
if [[ -z "$after" ]]; then
  echo "== Remaining: none. The project builds clean."
  exit 0
fi

echo "== Remaining: $(wc -l <<<"$after") error(s) to fix by hand"
echo "$after"
echo
echo "Each message says what to write instead. Rule docs with examples: FunctionalLint/docs/rules/<ID>.md"
exit 1
