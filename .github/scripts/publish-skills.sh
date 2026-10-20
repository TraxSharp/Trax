#!/usr/bin/env bash
# Commits a staged skills tree to a branch and tags it
# (Trax.Docs/adr/0056-agent-skills-are-written-in-the-docs-and-published-from-a-release.md).
#
#   publish-skills.sh <staged dir> <owner/repo> <branch> <tag> <commit headline>
#
# The branch must exist: it is created once, by hand, with a signed commit. GH_TOKEN must be
# able to write the repository's contents. The commit goes through GraphQL createCommitOnBranch,
# so GitHub signs it and the repository's verified-signatures rule holds without a key in this
# job. The tree replaces what is there: a file the release no longer stages is deleted. A re-run
# of the same release finds the tree unchanged and the tag in place, and does nothing.
set -euo pipefail

dir=$1
repo=$2
branch=$3
tag=$4
headline=$5
work=$(mktemp -d)

if ! head=$(gh api "repos/$repo/git/ref/heads/$branch" --jq .object.sha 2>/dev/null); then
  echo "::error::$repo has no $branch branch. Create it once with a signed commit (see the ADR) and re-run this job."
  exit 1
fi
tree=$(gh api "repos/$repo/git/commits/$head" --jq .tree.sha)

gh api "repos/$repo/git/trees/$tree?recursive=1" \
  --jq '.tree[] | select(.type == "blob") | .path + "\t" + .sha' | sort > "$work/existing"
(cd "$dir" && find . -type f -printf '%P\n' | sort | while IFS= read -r f; do
  printf '%s\t%s\n' "$f" "$(git hash-object -- "$f")"
done) > "$work/staged"

if cmp -s "$work/existing" "$work/staged"; then
  echo "$repo $branch already holds this tree."
  commit=$head
else
  cut -f1 "$work/staged" | while IFS= read -r f; do
    jq -n --arg path "$f" --rawfile contents <(base64 -w0 -- "$dir/$f") '{path: $path, contents: $contents}'
  done | jq -s . > "$work/additions.json"
  comm -23 <(cut -f1 "$work/existing") <(cut -f1 "$work/staged") \
    | jq -R '{path: .}' | jq -s . > "$work/deletions.json"

  jq -n \
    --arg repo "$repo" --arg branch "$branch" --arg head "$head" --arg headline "$headline" \
    --slurpfile additions "$work/additions.json" --slurpfile deletions "$work/deletions.json" \
    '{
      query: "mutation($input: CreateCommitOnBranchInput!) { createCommitOnBranch(input: $input) { commit { oid } } }",
      variables: { input: {
        branch: { repositoryNameWithOwner: $repo, branchName: $branch },
        expectedHeadOid: $head,
        message: { headline: $headline },
        fileChanges: { additions: $additions[0], deletions: $deletions[0] }
      } }
    }' > "$work/body.json"

  commit=$(gh api graphql --input "$work/body.json" --jq .data.createCommitOnBranch.commit.oid)
  if [ -z "$commit" ] || [ "$commit" = null ]; then
    echo "::error::createCommitOnBranch returned no commit for $repo."
    exit 1
  fi
  echo "Committed $commit to $repo $branch."
fi

if existing_tag=$(gh api "repos/$repo/git/ref/tags/$tag" --jq .object.sha 2>/dev/null); then
  if [ "$existing_tag" != "$commit" ]; then
    echo "::error::$repo already has $tag at $existing_tag, not $commit. A tag is never moved; find out who created it."
    exit 1
  fi
  echo "$repo already has $tag at $commit."
else
  gh api "repos/$repo/git/refs" -f ref="refs/tags/$tag" -f sha="$commit" --silent
  echo "Tagged $commit as $tag in $repo."
fi
