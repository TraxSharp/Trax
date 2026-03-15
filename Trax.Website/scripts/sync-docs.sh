#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
CACHE_DIR="$ROOT_DIR/.docs-cache"
CLONE_DIR="$ROOT_DIR/.docs-clone"

# 1. Local workspace (monorepo development)
# 2. Shallow clone from GitHub (CI / Vercel)
LOCAL_DOCS="$(dirname "$ROOT_DIR")/Trax.Docs"
REPO_URL="https://github.com/TraxSharp/Trax.Docs.git"

if [ -d "$LOCAL_DOCS" ]; then
  SOURCE_DIR="$LOCAL_DOCS"
  echo "Using local workspace docs: $SOURCE_DIR"
else
  echo "Cloning Trax.Docs main branch..."
  rm -rf "$CLONE_DIR"
  git clone --depth 1 --branch main "$REPO_URL" "$CLONE_DIR" 2>&1
  SOURCE_DIR="$CLONE_DIR"
  echo "Using cloned docs: $SOURCE_DIR"
fi

# Clean and recreate cache
rm -rf "$CACHE_DIR"
mkdir -p "$CACHE_DIR"

# Copy all markdown files preserving directory structure
cd "$SOURCE_DIR"
find . -name "*.md" -not -name "README.md" | while read -r file; do
  dir=$(dirname "$file")
  mkdir -p "$CACHE_DIR/$dir"
  cp "$file" "$CACHE_DIR/$file"
done

# Clean up clone if we made one
[ -d "$CLONE_DIR" ] && rm -rf "$CLONE_DIR"

echo "Synced docs to $CACHE_DIR"
find "$CACHE_DIR" -name "*.md" | wc -l | xargs -I{} echo "{} markdown files copied"
