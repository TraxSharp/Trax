#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
CACHE_DIR="$ROOT_DIR/.docs-cache"

# Prefer the local workspace repo (../Trax.Docs) over the git submodule.
# In the monorepo workspace, Trax.Docs/ sits next to Trax.Website/ and
# always has the latest local edits. The submodule is a fallback for CI
# or standalone checkouts where the workspace isn't available.
LOCAL_DOCS="$(dirname "$ROOT_DIR")/Trax.Docs"
SUBMODULE_DOCS="$ROOT_DIR/docs-source"

if [ -d "$LOCAL_DOCS" ]; then
  SOURCE_DIR="$LOCAL_DOCS"
  echo "Using local workspace docs: $SOURCE_DIR"
elif [ -d "$SUBMODULE_DOCS" ]; then
  SOURCE_DIR="$SUBMODULE_DOCS"
  echo "Using submodule docs: $SOURCE_DIR"
else
  echo "Error: No docs source found. Either place Trax.Docs next to Trax.Website or run 'git submodule update --init'."
  exit 1
fi

# Clean and recreate cache
rm -rf "$CACHE_DIR"
mkdir -p "$CACHE_DIR"

# Copy all markdown files preserving directory structure
# Skip Jekyll-only files
cd "$SOURCE_DIR"
find . -name "*.md" -not -name "README.md" | while read -r file; do
  dir=$(dirname "$file")
  mkdir -p "$CACHE_DIR/$dir"
  cp "$file" "$CACHE_DIR/$file"
done

echo "Synced docs to $CACHE_DIR"
find "$CACHE_DIR" -name "*.md" | wc -l | xargs -I{} echo "{} markdown files copied"
