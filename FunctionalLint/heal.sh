#!/usr/bin/env bash
# Auto-fix what FunctionalLint can fix safely, then report what's left and how to avoid it next time.
#
#   ./FunctionalLint/heal.sh path/to/Core.csproj           # fix, then report
#   ./FunctionalLint/heal.sh path/to/App.slnx --check      # report only, change nothing
#   ./FunctionalLint/heal.sh --hook < hook-input.json      # agent hook (see agents/claude-code-hook.sh)
#   ./FunctionalLint/heal.sh --serve                       # keep projects loaded: checks drop from ~4 s to ~0.5 s
#
# Exit code 0 means clean, 1 means problems remain (listed at the end), 2 means it couldn't run.
# The work is done by tools/FunctionalLint.Heal, which loads the project once and fixes everything in memory.
# It's run from its built dll (`dotnet run` would add ~2 s per call), rebuilt whenever FunctionalLint's source changes.
set -uo pipefail
# The physical path (-P): building one folder through two paths (say, via a symlink) makes MSBuild's incremental
# clean delete the other path's outputs, such as the runtimeconfig.json the dll needs to start.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
tool="$here/tools/FunctionalLint.Heal"
dll="$tool/bin/Release/net11.0/FunctionalLint.Heal.dll"

if [[ ! -f "$dll" || ! -f "${dll%.dll}.runtimeconfig.json" ]] \
   || [[ -n "$(find "$here/src" "$tool" -name '*.cs' -newer "$dll" -not -path '*/obj/*' -print -quit)" ]]; then
  if ! dotnet build "$tool" -c Release -v q -nologo >/dev/null 2>&1; then
    echo "FunctionalLint: couldn't build the heal tool. Run: dotnet build $tool -c Release" >&2
    exit 2
  fi
fi

exec dotnet "$dll" "$@"
