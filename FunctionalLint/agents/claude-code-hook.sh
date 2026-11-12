#!/usr/bin/env bash
# Claude Code PostToolUse hook: after Claude edits a .cs file in a functional core project, check that
# project and hand any problems back to Claude (exit 2 + stderr). Non-functional projects are ignored.
# See README.md ("Agents") for the settings.json entry.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
"$here/../heal.sh" --hook
status=$?
# heal.sh exits 2 both for "problems found" (hook mode) and "couldn't run". Either way Claude should see stderr.
exit $status
