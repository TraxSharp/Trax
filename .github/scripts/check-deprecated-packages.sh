#!/usr/bin/env bash
# Fails when the restored graph resolves a package its owner has deprecated as
# CriticalBugs or Legacy.
#
# NuGetAudit only sees advisories recorded in nuget.org's vulnerability feed. An
# owner can instead deprecate the affected versions and point at an advisory that
# lives only on their own repository, and then NU1901-NU1904 never fire. The
# deprecation is the signal that is left, so it is checked here.
#
# The packages come from the committed lockfiles, which the locked restore before
# this step has just held the graph to, and each id and version is looked up once,
# several at a time. `dotnet list package --deprecated` evaluated every project in
# the solution in turn and took well over a minute to check the same few hundred
# packages. A lookup that fails fails the check: an unchecked package is not a
# clean one.
#
# Run after `dotnet restore --locked-mode`, from the folder whose lockfiles to check.
#
# One folder is left out: Trax.Analyzers/tests/Trax.Analyzers.Tests.Pins. Its projects build
# nothing and run nothing; they pin, through lockfiles, the reference packages the analyzer tests
# compile sample code against, among them deliberately old ones (.NET 5, xunit v2, MSTest 4.3.3)
# that some rules are tested on. Nothing from that folder reaches a build or a package.
set -euo pipefail

registration=https://api.nuget.org/v3/registration5-gz-semver2

# Prints "<id> <version> (<reasons>)" when the package version is deprecated as
# CriticalBugs or Legacy, nothing when it is not, and fails when it cannot tell.
check() {
  local id=$1 version=$2 leaf entry
  leaf=$(curl -fsS --compressed --retry 3 \
    "$registration/${id,,}/${version,,}.json") || {
    echo "::error::Could not look up $id $version on nuget.org." >&2
    return 1
  }
  entry=$(jq -r '.catalogEntry' <<<"$leaf")
  curl -fsS --compressed --retry 3 "$entry" | jq -r --arg id "$id" --arg v "$version" '
    (.deprecation.reasons // []) as $reasons
    | select($reasons | any(. == "CriticalBugs" or . == "Legacy"))
    | "\($id) \($v) (\($reasons | join(", ")))"' || {
    echo "::error::Could not read the catalog entry of $id $version." >&2
    return 1
  }
}
export -f check
export registration

packages=$(
  git ls-files -z '*packages.lock.json' ':(top,exclude)Trax.Analyzers/tests/Trax.Analyzers.Tests.Pins/' |
    xargs -0 jq -r '.dependencies[] | to_entries[]
      | select(.value.type != "Project" and .value.resolved != null)
      | "\(.key) \(.value.resolved)"' |
    sort -u
)

if [ -z "$packages" ]; then
  echo "::error::No packages found in the committed lockfiles."
  exit 1
fi

deprecated=$(xargs -P 16 -L 1 bash -c 'check "$0" "$1"' <<<"$packages")

if [ -n "$deprecated" ]; then
  echo "::error::The restore resolves packages deprecated as CriticalBugs or Legacy:"
  sort <<<"$deprecated"
  exit 1
fi

echo "No package in the graph is deprecated as CriticalBugs or Legacy ($(wc -l <<<"$packages" | tr -d ' ') checked)."
