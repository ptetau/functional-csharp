#!/usr/bin/env bash
# Scale test: builds one large functional core and times the analyzer and the gate on it.
#
#   FunctionalLint/tools/scale-test.sh [copies] [folder]
#
# The core is the four sample apps' cores copied `copies` times (default 25) into one project, each copy in its own
# namespace, so it's real code that follows the rules: about 2,900 lines per copy. It's written to `folder` (default:
# a new temporary folder) with its own policy, and the timings are printed at the end. Nothing in the repository
# changes. Results are recorded in docs/performance.md.
set -euo pipefail
copies="${1:-25}"
out="${2:-$(mktemp -d)}"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
core="$out/Scale.Core"
rm -rf "$core" && mkdir -p "$core"

cat > "$out/functionallint.policy" <<POLICY
is_global = true
global_level = 200
functional_lint.cores = Scale.Core/Scale.Core.csproj
functional_lint.may_lower = none
functional_lint.legacy_namespaces = none
functional_lint.adapter_namespaces = Functional.Adapters
functional_lint.allowed_references = Functional
POLICY

cat > "$core/Scale.Core.csproj" <<PROJECT
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <Import Project="$repo/FunctionalLint/build/FunctionalCore.props" />
  <ItemGroup>
    <ProjectReference Include="$repo/samples/Functional/Functional.csproj" />
  </ItemGroup>
</Project>
PROJECT

for app in "$repo"/samples/*/*.Core; do
  name="$(basename "$app")"          # Ledger.Core
  for ((i = 0; i < copies; i++)); do
    dest="$core/$name/Copy$i"
    mkdir -p "$dest"
    find "$app" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | while read -r file; do
      sed -E "s/\\b${name//./\\.}\\b/$name.Copy$i/g" "$file" > "$dest/$(basename "$file")"
    done
  done
done

files=$(find "$core" -name '*.cs' | wc -l)
lines=$(find "$core" -name '*.cs' -exec cat {} + | wc -l)
echo "Generated $files files, $lines lines in $core"

seconds() { local start end; start=$(date +%s.%N); "$@" > "$out/last.log" 2>&1 || { cat "$out/last.log"; exit 1; }; end=$(date +%s.%N); echo "$end - $start" | bc; }

# Warm up: restore, and build the Functional library once.
dotnet build "$core" -p:RunAnalyzers=false -v q -nologo > /dev/null
without=$(seconds dotnet build "$core" --no-incremental -p:RunAnalyzers=false -v q -nologo)
with=$(seconds dotnet build "$core" --no-incremental -p:ReportAnalyzer=true -v d -nologo)
grep -E "^\s+[0-9.]+\s+[0-9]+\s+FunctionalLint" "$out/last.log" | head -3 > "$out/analyzer.txt" || true
FUNCTIONALLINT_NO_SERVER=1 "$repo/FunctionalLint/heal.sh" --help > /dev/null 2>&1 || true   # builds the heal tool
gate=$(FUNCTIONALLINT_POLICY="$out/functionallint.policy" FUNCTIONALLINT_NO_SERVER=1 seconds "$repo/FunctionalLint/heal.sh" "$core/Scale.Core.csproj" --check)

echo
echo "Lines of code:              $lines"
echo "Build without the analyzer: ${without}s"
echo "Build with the analyzer:    ${with}s"
echo "Gate (heal.sh --check):     ${gate}s"
echo "Analyzer time, as the compiler reports it:"
cat "$out/analyzer.txt"
