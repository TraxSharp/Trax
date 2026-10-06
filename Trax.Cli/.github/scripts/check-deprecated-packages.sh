#!/usr/bin/env bash
# Fails when the restored graph resolves a package its owner has deprecated as
# CriticalBugs or Legacy.
#
# NuGetAudit only sees advisories recorded in nuget.org's vulnerability feed. An
# owner can instead deprecate the affected versions and point at an advisory that
# lives only on their own repository, and then NU1901-NU1904 never fire. The
# deprecation is the signal that is left, so it is checked here.
#
# Run after `dotnet restore`. Takes the solution file as its only argument.
set -euo pipefail

solution="$1"

deprecated=$(
  dotnet list "$solution" package --deprecated --include-transitive --format json |
    jq -r '
      [ .projects[]? | .frameworks[]?
        | (.topLevelPackages // []) + (.transitivePackages // []) | .[]
        | select((.deprecationReasons // []) | any(. == "CriticalBugs" or . == "Legacy")) ]
      | unique_by(.id + "@" + .resolvedVersion)
      | .[] | "\(.id) \(.resolvedVersion) (\(.deprecationReasons | join(", ")))"'
)

if [ -n "$deprecated" ]; then
  echo "::error::The restore resolves packages deprecated as CriticalBugs or Legacy:"
  echo "$deprecated"
  exit 1
fi

echo "No package in the graph is deprecated as CriticalBugs or Legacy."
