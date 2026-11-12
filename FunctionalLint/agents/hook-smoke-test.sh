#!/usr/bin/env bash
# End-to-end check of the agent hook and the check server, run as real processes the way Claude Code runs them:
#
#   FunctionalLint/agents/hook-smoke-test.sh path/to/Core/SomeFile.cs
#
# The file must be in a functional core project that's clean. A scratch file is added next to it, then removed.
# Steps: a clean edit passes and starts the server; an edit with a loop fails with FP0001; the rewrite passes.
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
heal="$here/../heal.sh"
fail() { echo "FAIL: $*" >&2; exit 1; }

[[ $# -eq 1 && -f "$1" ]] || fail "usage: $0 <a .cs file in a clean functional core project>"
dir="$(cd "$(dirname "$1")" && pwd)"
native() { if command -v cygpath >/dev/null; then cygpath -m "$1"; else echo "$1"; fi; }   # Git Bash on Windows
hook() { printf '{"tool_input":{"file_path":"%s"}}' "$(native "$1")" | "$here/claude-code-hook.sh"; }

echo "1. A clean edit passes and starts the check server"
hook "$dir/$(basename "$1")" || fail "the clean file was rejected"
for _ in $(seq 1 120); do "$heal" --server-status >/dev/null && break; sleep 1; done
"$heal" --server-status || fail "the check server didn't start"

scratch="$dir/HookSmokeTest.cs"
trap 'rm -f "$scratch"' EXIT
echo "2. An edit with a loop fails with FP0001"
cat > "$scratch" <<'CS'
namespace HookSmokeTest;

public static class Scratch
{
    public static int Sum(IReadOnlyList<int> xs)
    {
        var total = 0;
        foreach (var x in xs) total += x;
        return total;
    }
}
CS
out="$(hook "$scratch" 2>&1)"; code=$?
[[ $code -eq 2 ]] || fail "expected exit 2 for a loop, got $code: $out"
[[ $out == *FP0001* ]] || fail "expected FP0001 in: $out"
echo "$out" | sed 's/^/   /'

echo "3. The functional rewrite passes"
cat > "$scratch" <<'CS'
namespace HookSmokeTest;

public static class Scratch
{
    public static int Sum(IReadOnlyList<int> xs) => xs.Sum();
}
CS
out="$(hook "$scratch" 2>&1)" || fail "the rewrite was rejected: $out"
"$heal" --server-status >/dev/null || fail "the check server stopped"
echo "OK"
