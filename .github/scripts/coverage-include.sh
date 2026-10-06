#!/usr/bin/env bash
# Prints the coverlet Include filter for one folder: "[Assembly]*" for every non-test project in it.
#
# Coverage measures a folder's own code. Without the filter coverlet instruments every assembly
# whose source it can find, which in this repository means every upstream Trax folder too: the
# numbers then credit a folder with code it does not own, and the instrumented upstream code
# made Trax.Samples' tests five times slower.
#
# Usage, from the repository root: coverage-include.sh <folder>
set -euo pipefail
cd "$1"
git ls-files '*.csproj' | grep -v -E '(^|/)tests/|(^|/)templates/content/|(^|/)plugins/' | while IFS= read -r p; do
  name=$(sed -n 's:.*<AssemblyName>\(.*\)</AssemblyName>.*:\1:p' "$p" | head -1)
  echo "[${name:-$(basename "$p" .csproj)}]*"
done | paste -sd, -
