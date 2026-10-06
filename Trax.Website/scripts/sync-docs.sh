#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
CACHE_DIR="$ROOT_DIR/.docs-cache"
CLONE_DIR="$ROOT_DIR/.docs-clone"

# Where the docs come from:
#   1. A sibling ../Trax.Docs checkout (local development), used as it is on disk.
#   2. Otherwise a shallow clone of Trax.Docs main (CI and Vercel).
#
# The clone is deliberately not pinned. A push to Trax.Docs main fires the Vercel
# deploy hook, and the deploy it starts must pick up that push, so whatever is on
# Trax.Docs main is effectively website content: it is published on the next
# deploy without a change in this repo. Review Trax.Docs pull requests with that
# in mind. Pages are rendered as CommonMark with an allow-list for raw HTML
# (src/lib/mdx-options.ts).
LOCAL_DOCS="$(dirname "$ROOT_DIR")/Trax.Docs"
REPO_URL="https://github.com/TraxSharp/Trax.Docs.git"

if [ -d "$LOCAL_DOCS" ]; then
  SOURCE_DIR="$LOCAL_DOCS"
  echo "Using local workspace docs: $SOURCE_DIR"
else
  echo "Cloning Trax.Docs main branch..."
  rm -rf "$CLONE_DIR"
  if ! git clone --depth 1 --branch main "$REPO_URL" "$CLONE_DIR" 2>&1; then
    echo "error: could not clone $REPO_URL (branch main); the docs cannot be built without it" >&2
    exit 1
  fi
  SOURCE_DIR="$CLONE_DIR"
  echo "Using cloned docs: $SOURCE_DIR"
fi

# Clean and recreate cache
rm -rf "$CACHE_DIR"
mkdir -p "$CACHE_DIR"

# Copy all markdown files preserving directory structure.
#
# Everything under Trax.Docs is published EXCEPT the engineering-record trees below.
# find descends into dotfile directories, so .claude/ must be named explicitly or the
# agent skill and the ADR format spec get public /docs/ routes.
cd "$SOURCE_DIR"
find . -name "*.md" -not -name "README.md" \
  -not -path "./adr/*" \
  -not -path "./.claude/*" \
  -not -path "./tools/*" \
  -not -path "./tests/*" \
  -not -path "./.github/*" \
  | while read -r file; do
  dir=$(dirname "$file")
  mkdir -p "$CACHE_DIR/$dir"
  cp "$file" "$CACHE_DIR/$file"
done

# ADR file names, so a citation such as `Trax.Docs/adr/0026` in a page links to the
# exact file on GitHub. ADRs themselves are not published as pages.
node "$SCRIPT_DIR/adr-index.mjs" "$SOURCE_DIR" "$(dirname "$ROOT_DIR")" > "$CACHE_DIR/adr-index.json"

# Clean up clone if we made one
rm -rf "$CLONE_DIR"

count=$(find "$CACHE_DIR" -name "*.md" | wc -l | tr -d ' ')
if [ "$count" -eq 0 ] || [ ! -f "$CACHE_DIR/index.md" ]; then
  echo "error: no docs were copied from $SOURCE_DIR (expected index.md and the page tree)" >&2
  exit 1
fi
echo "Synced docs to $CACHE_DIR"
echo "$count markdown files copied"
