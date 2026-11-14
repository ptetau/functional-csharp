#!/usr/bin/env bash
# Claude Code Stop hook: before Claude finishes, check every functional core that has changed files (git status),
# however they were changed, including by a shell command the edit hook never sees. Problems stop Claude once
# (exit 2) so it fixes them; if it stops again with problems left, they're shown to you instead (exit 1).
# See README.md ("Agents") for the settings.json entry.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
"$here/../heal.sh" --stop
