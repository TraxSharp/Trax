#!/usr/bin/env bash
# Fails if any critical disclaimer location stops containing "NO WARRANTY".
# Run from Trax.Api/ repo root or from CI.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MARKER="NO WARRANTY"
EXIT=0

check() {
    local label="$1"
    local path="$2"
    if [ ! -f "$path" ]; then
        echo "MISSING: $label ($path)"
        EXIT=1
        return
    fi
    if ! grep -F -q "$MARKER" "$path"; then
        echo "MISSING DISCLAIMER: $label ($path)"
        EXIT=1
    else
        echo "ok: $label"
    fi
}

check "SECURITY-DISCLAIMER.md" "$ROOT/SECURITY-DISCLAIMER.md"
check "README.md" "$ROOT/README.md"
check "CHANGELOG.md" "$ROOT/CHANGELOG.md"

for csproj in \
    "$ROOT/src/Trax.Api.Auth/Trax.Api.Auth.csproj" \
    "$ROOT/src/Trax.Api.Auth.ApiKey/Trax.Api.Auth.ApiKey.csproj" \
    "$ROOT/src/Trax.Api.GraphQL.Audit/Trax.Api.GraphQL.Audit.csproj"
do
    check "$(basename "$csproj")" "$csproj"
done

# Every public-type .cs file in the three new packages must carry the marker
# in at least one XML <remarks> block.
for src_dir in \
    "$ROOT/src/Trax.Api.Auth" \
    "$ROOT/src/Trax.Api.Auth.ApiKey" \
    "$ROOT/src/Trax.Api.GraphQL.Audit"
do
    while IFS= read -r -d '' file; do
        if ! grep -F -q "$MARKER" "$file"; then
            echo "MISSING DISCLAIMER: $file"
            EXIT=1
        fi
    done < <(find "$src_dir" -name "*.cs" -not -path "*/bin/*" -not -path "*/obj/*" -print0)
done

if [ "$EXIT" -eq 0 ]; then
    echo ""
    echo "All disclaimer checks passed."
fi
exit $EXIT
