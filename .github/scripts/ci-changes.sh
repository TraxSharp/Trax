#!/usr/bin/env bash
# Decides which folders CI runs, and writes the job matrices for them to $GITHUB_OUTPUT.
#
# A folder runs when a file in it changed, or when anything upstream of it did
# (.github/ci/packages.json names each folder's upstream, and the closure is taken
# here). A change outside every folder, such as a workflow, global.json or this
# script, runs everything, and so does any event other than a pull request.
#
# Usage, from the repository root: ci-changes.sh <event-name>
# On a pull request the checkout must be the merge commit with its first parent
# fetched (fetch-depth: 2), so HEAD^1 is the base the pull request merges into.
set -euo pipefail

event="$1"
config=.github/ci/packages.json
all=$(jq -c '[.packages[].folder]' "$config")

if [ "$event" = pull_request ]; then
  changed=$(git diff --name-only HEAD^1 HEAD | jq -R . | jq -sc .)
  selected=$(jq -c --argjson files "$changed" --argjson all "$all" -n '
    ($files | map(split("/")[0])) as $tops
    | if ($tops | any(. as $t | $all | index($t) | not)) then $all
      else ($tops | unique) end')
else
  selected=$all
fi

# Add every folder with something already selected upstream of it, until nothing changes.
selected=$(jq -c --argjson selected "$selected" '
  .packages as $packages
  | def grow: . as $s
      | ($s + [$packages[] | select(any(.upstream[]; . as $u | $s | index($u))) | .folder]
         | unique) as $next
      | if ($next | length) == ($s | length) then $s else ($next | grow) end;
  $selected | grow' "$config")

dotnet=$(jq -c --argjson selected "$selected" \
  '[.packages[] | select(.dotnet and (.folder as $f | $selected | index($f))) | {folder} + .dotnet]' "$config")
adr=$(jq -c --argjson selected "$selected" \
  '[.packages[] | select(.adr and (.folder as $f | $selected | index($f))) | {folder} + .adr]' "$config")

echo "Running: $(jq -r 'join(", ")' <<<"$selected")"

{
  echo "folders=$selected"
  echo "dotnet=$dotnet"
  echo "adr=$adr"
} >>"${GITHUB_OUTPUT:-/dev/stdout}"
