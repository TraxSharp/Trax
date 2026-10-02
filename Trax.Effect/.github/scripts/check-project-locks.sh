#!/usr/bin/env bash
# Fails when a tracked project would escape the locked restore.
#
# `dotnet restore --locked-mode` checks a lockfile only where one exists: a
# project committed without packages.lock.json restores unlocked and passes. And
# a project missing from the solution is never restored by CI at all, so its
# committed lockfile goes stale unseen. Every tracked csproj therefore has to be
# in the solution and have a committed lockfile beside it, unless it is listed in
# .github/unlocked-projects.txt with the reason it is built elsewhere.
#
# Run after `dotnet restore --locked-mode`: with RestorePackagesWithLockFile on,
# that restore writes a lockfile for any project lacking one, which the last
# check below catches as well.
set -euo pipefail

solution="$1"
allowlist=.github/unlocked-projects.txt
failed=0

# The solution with its XML comments removed, so a project commented out of it does
# not count as being in it.
projects_in_solution=$(perl -0777 -pe 's/<!--.*?-->//gs' "$solution")

while IFS= read -r project; do
  if [ -f "$allowlist" ] && grep -qxF "$project" <(sed 's/[[:space:]]*#.*$//' "$allowlist"); then
    continue
  fi

  if ! git ls-files --error-unmatch "$(dirname "$project")/packages.lock.json" >/dev/null 2>&1; then
    echo "::error file=$project::No committed packages.lock.json beside this project."
    failed=1
  fi

  if ! grep -qF "Path=\"$project\"" <<<"$projects_in_solution"; then
    echo "::error file=$project::Not in $solution, so CI never restores it."
    failed=1
  fi
done < <(git ls-files '*.csproj')

changed=$(git status --porcelain -- '*packages.lock.json')
if [ -n "$changed" ]; then
  echo "::error::The locked restore created or changed lockfiles:"
  echo "$changed"
  failed=1
fi

exit "$failed"
