#!/usr/bin/env bash
# Claude Code PostToolUse hook: after Claude edits a .cs file in a functional core project, check that
# project and hand any problems back to Claude (exit 2 + stderr). Non-functional projects are ignored.
# See README.md ("Agents") for the settings.json entry.
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
tool="$here/../tools/FunctionalLint.Heal"
dll="$tool/bin/Release/net11.0/FunctionalLint.Heal.dll"

# Build the checker the first time, and again whenever FunctionalLint's own source changes.
if [[ ! -f "$dll" ]] || [[ -n "$(find "$here/../src" "$tool" -name '*.cs' -newer "$dll" -not -path '*/obj/*' -print -quit)" ]]; then
  dotnet build "$tool" -c Release -v q -nologo >/dev/null 2>&1 || { echo "FunctionalLint hook: couldn't build the checker." >&2; exit 0; }
fi

exec dotnet "$dll" --hook
