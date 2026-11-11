#!/usr/bin/env bash
# Auto-fix what FunctionalLint can fix safely, then report what's left and how to avoid it next time.
#
#   ./FunctionalLint/heal.sh path/to/Core.csproj           # fix, then report
#   ./FunctionalLint/heal.sh path/to/App.slnx --check      # report only, change nothing
#
# Exit code 0 means clean, 1 means problems remain (listed at the end), 2 means it couldn't run.
# The work is done by tools/FunctionalLint.Heal, which loads the project once and fixes everything in memory.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec dotnet run --project "$here/tools/FunctionalLint.Heal" -c Release -v q -- "$@"
