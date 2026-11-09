#!/usr/bin/env bash
# Fetches the release tags DocsPackageVersionsTests reads: the version the docs pin Trax
# packages at, and the newest release. The test lists the packages each tag's tree builds
# (git ls-tree), so a page can never pin a package that version did not ship. CI's checkout is
# one commit with no tags; this adds those two tags, one commit deep, and nothing else.
#
# Usage, from the repository root: fetch-docs-release-tags.sh
set -euo pipefail

remote_tags=$(git ls-remote --tags --refs origin 'v*' | sed 's#.*refs/tags/##' \
  | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' || true)
if [ -z "$remote_tags" ]; then
  echo "::error::origin has no v* release tags."
  exit 1
fi
latest=$(printf '%s\n' "$remote_tags" | sort -V | tail -n 1)

pinned=$(grep -rhoE 'Include="Trax\.[^"]+"[[:space:]]+Version="[0-9]+\.[0-9]+\.[0-9]+"' \
  --include='*.md' --exclude-dir=adr Trax.Docs | sed -E 's/.*Version="([^"]+)"/v\1/' | sort -u)

refspecs=("+refs/tags/$latest:refs/tags/$latest")
for tag in $pinned; do
  # A pin with no tag is left for the test to name, with the page it is on.
  if printf '%s\n' "$remote_tags" | grep -qxF "$tag"; then
    refspecs+=("+refs/tags/$tag:refs/tags/$tag")
  fi
done

git fetch --no-tags --depth=1 origin "${refspecs[@]}"
echo "Fetched $(printf '%s ' "${refspecs[@]##*:refs/tags/}")"
