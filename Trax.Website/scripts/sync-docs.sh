#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
CACHE_DIR="$ROOT_DIR/.docs-cache"
REPO_ROOT="$(dirname "$ROOT_DIR")"

# The docs are Trax.Docs in this same checkout, so the site is built from exactly the
# commit it was deployed from. Production deploys from the `website` branch, which the
# release workflow moves to each release tag (.github/workflows/website.yml), so the
# published docs describe released code. Pages are rendered as CommonMark with an
# allow-list for raw HTML (src/lib/mdx-options.ts).
SOURCE_DIR="$REPO_ROOT/Trax.Docs"
if [ ! -d "$SOURCE_DIR" ]; then
  echo "error: $SOURCE_DIR not found; the site is built from the Trax repository checkout" >&2
  exit 1
fi
echo "Using docs: $SOURCE_DIR"

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

# Every Trax package releases at one version, and a page names it in each
# <PackageReference Include="Trax.*" Version="..."> it shows. A production build stamps
# the version of the latest release into those, so the site always tells a reader to
# install what exists; the version written in Trax.Docs is only where it was last edited.
# TRAX_DOCS_VERSION overrides it; outside production nothing is stamped.
if [ -n "${TRAX_DOCS_VERSION:-}" ] || [ "${VERCEL_ENV:-}" = "production" ]; then
  version="${TRAX_DOCS_VERSION:-}"
  if [ -z "$version" ]; then
    version=$(curl -fsS --retry 3 https://api.github.com/repos/TraxSharp/Trax/releases/latest \
      | node -e 'let s="";process.stdin.on("data",d=>s+=d).on("end",()=>process.stdout.write(JSON.parse(s).tag_name.replace(/^v/,"")))')
  fi
  if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "error: '$version' is not a release version; refusing to stamp the docs with it" >&2
    exit 1
  fi
  find "$CACHE_DIR" -name "*.md" -print0 | xargs -0 perl -pi -e \
    's/(<PackageReference\s+Include="Trax\.[^"]+"\s+Version=")[^"]+"/${1}'"$version"'"/g'
  echo "Stamped Trax package versions with $version"
fi

# ADR file names, so a citation such as `Trax.Docs/adr/0026` in a page links to the
# exact file on GitHub. ADRs themselves are not published as pages.
node "$SCRIPT_DIR/adr-index.mjs" "$REPO_ROOT" > "$CACHE_DIR/adr-index.json"

count=$(find "$CACHE_DIR" -name "*.md" | wc -l | tr -d ' ')
if [ "$count" -eq 0 ] || [ ! -f "$CACHE_DIR/index.md" ]; then
  echo "error: no docs were copied from $SOURCE_DIR (expected index.md and the page tree)" >&2
  exit 1
fi
echo "Synced docs to $CACHE_DIR"
echo "$count markdown files copied"
