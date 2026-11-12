#!/usr/bin/env bash
# Runs Claude Code headless on fixed tasks in a fresh functional core project, with the recommended setup
# (policy, permission rules, hook, agent guide), then scores each run. Costs tokens: run it on demand.
#
#   FunctionalLint/agents/eval/run.sh [output-folder] [task-id ...]
#
# Needs the `claude` CLI and the .NET SDK. Writes transcripts and results.md to the output folder.
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
lint="$(cd "$here/../.." && pwd -P)"
repo="$(cd "$lint/.." && pwd -P)"
out="${1:-$(mktemp -d)}"; shift || true
mkdir -p "$out"; out="$(cd "$out" && pwd -P)"

fixture="$out/fixture"
rm -rf "$fixture"; mkdir -p "$fixture/Core" "$fixture/.claude"
ln -s "$lint" "$fixture/FunctionalLint"
cp "$repo/global.json" "$fixture/"
cp "$lint/agents/claude-settings.json" "$fixture/.claude/settings.json"
cp "$lint/agents/AGENTS.md" "$fixture/CLAUDE.md"
cat > "$fixture/functionallint.policy" <<'POLICY'
is_global = true
global_level = 200
functional_lint.cores = Core/Core.csproj
functional_lint.may_lower = none
functional_lint.legacy_namespaces = none
functional_lint.adapter_namespaces = none
functional_lint.allowed_references = Functional
POLICY
cat > "$fixture/Core/Core.csproj" <<PROJECT
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <Import Project="../FunctionalLint/build/FunctionalCore.props" />
  <ItemGroup><ProjectReference Include="$repo/samples/Functional/Functional.csproj" /></ItemGroup>
</Project>
PROJECT
cat > "$fixture/Core/Orders.cs" <<'CS'
using Functional;

namespace Shop;

public sealed record Line(string Sku, int Quantity, decimal UnitPrice);

public sealed record Order(string Id, ValueList<Line> Lines);
CS
(cd "$fixture" && git init -q && git add -A && git -c user.name=eval -c user.email=eval@localhost commit -qm fixture)
if ! dotnet build "$fixture/Core" -v q -nologo >/dev/null 2>&1; then echo "The fixture doesn't build." >&2; exit 2; fi
FUNCTIONALLINT_NO_SERVER=1 "$lint/heal.sh" "$fixture/Core/Core.csproj" --check >/dev/null || { echo "The fixture isn't clean." >&2; exit 2; }

grep -v '^\s*#' "$here/tasks.txt" | grep -v '^\s*$' | while IFS='|' read -r id expect prompt; do
  id="$(echo "$id" | xargs)"; expect="$(echo "$expect" | xargs)"; prompt="$(echo "$prompt" | sed 's/^ *//')"
  if [[ $# -gt 0 && ! " $* " == *" $id "* ]]; then continue; fi
  work="$out/$id"; rm -rf "$work"; cp -a "$fixture" "$work"
  echo "== $id"
  (cd "$work" && timeout 900 claude -p "$prompt" --permission-mode acceptEdits \
      --allowedTools "Read,Edit,Write,Glob,Grep,Bash(dotnet build:*),Bash(dotnet test:*),Bash(FunctionalLint/heal.sh:*)" \
      --max-turns 40 --output-format stream-json --verbose > "$out/$id.jsonl" 2> "$out/$id.err" < /dev/null)
  (cd "$work" && FUNCTIONALLINT_NO_SERVER=1 "$lint/heal.sh" Core/Core.csproj --check > "$out/$id.gate" 2>&1; echo "exit=$?" >> "$out/$id.gate")
  (cd "$work" && dotnet build Core -v q -nologo > "$out/$id.build" 2>&1; echo "exit=$?" >> "$out/$id.build")
  printf '%s\n' "$expect" > "$out/$id.expect"
done

python3 -I "$here/score.py" "$out" | tee "$out/results.md"
