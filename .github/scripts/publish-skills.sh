#!/usr/bin/env bash
# Commits a staged skills tree on top of the live branch, to a staging branch, for a maintainer to
# approve (.github/workflows/skills.yml; Trax.Docs/adr/0056).
#
#   publish-skills.sh <staged dir> <owner/repo> <live branch> <staging branch> <commit headline>
#
# The staging branch is reset to the live branch's head and the tree is committed on it through
# GraphQL createCommitOnBranch, so GitHub signs the commit and the repository's verified-signatures
# rule holds without a key in this job. The tree replaces what is there: a file the release no
# longer stages is deleted. The commit's parent is the live head, so moving the live branch to it
# is a fast-forward. Prints what changes, and writes `commit=<sha>` to $GITHUB_OUTPUT: the new
# commit, or the live head when the tree is already live.
#
# GH_TOKEN must be able to write the staging branch. The live branch's ruleset must refuse it
# (Trax.Docs/contributing/writing-docs.md); the publish job moves that branch with a deploy key once
# approved.
set -euo pipefail

dir=$1
repo=$2
live=$3
staging=$4
headline=$5
work=$(mktemp -d)
out=${GITHUB_OUTPUT:-/dev/null}
summary=${GITHUB_STEP_SUMMARY:-/dev/null}

if ! head=$(gh api "repos/$repo/git/ref/heads/$live" --jq .object.sha 2>/dev/null); then
  echo "::error::$repo has no $live branch. Set it up as Trax.Docs/contributing/writing-docs.md 'Setting up the skills branch' says, then re-run."
  exit 1
fi
tree=$(gh api "repos/$repo/git/commits/$head" --jq .tree.sha)

gh api "repos/$repo/git/trees/$tree?recursive=1" \
  --jq '.tree[] | select(.type == "blob") | .path + "\t" + .sha' | sort > "$work/existing"
(cd "$dir" && find . -type f -printf '%P\n' | sort | while IFS= read -r f; do
  printf '%s\t%s\n' "$f" "$(git hash-object -- "$f")"
done) > "$work/staged"

if cmp -s "$work/existing" "$work/staged"; then
  echo "$repo $live already holds this tree; publishing tags $head."
  echo "commit=$head" >> "$out"
  exit 0
fi

# What the approver is asked to accept, by path.
tick='`'
{
  echo "### Changes to $tick$live$tick"
  echo
  comm -13 "$work/existing" "$work/staged" | cut -f1 | while IFS= read -r p; do echo "- added or changed: $tick$p$tick"; done
  comm -23 <(cut -f1 "$work/existing") <(cut -f1 "$work/staged") | while IFS= read -r p; do echo "- removed: $tick$p$tick"; done
} | tee -a "$summary"

# Start the staging branch at the live head, creating it the first time.
if gh api "repos/$repo/git/ref/heads/$staging" --silent 2>/dev/null; then
  gh api -X PATCH "repos/$repo/git/refs/heads/$staging" -f sha="$head" -F force=true --silent
else
  gh api "repos/$repo/git/refs" -f ref="refs/heads/$staging" -f sha="$head" --silent
fi

cut -f1 "$work/staged" | while IFS= read -r f; do
  jq -n --arg path "$f" --rawfile contents <(base64 -w0 -- "$dir/$f") '{path: $path, contents: $contents}'
done | jq -s . > "$work/additions.json"
comm -23 <(cut -f1 "$work/existing") <(cut -f1 "$work/staged") \
  | jq -R '{path: .}' | jq -s . > "$work/deletions.json"

jq -n \
  --arg repo "$repo" --arg branch "$staging" --arg head "$head" --arg headline "$headline" \
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
  echo "::error::createCommitOnBranch returned no commit for $repo $staging."
  exit 1
fi
echo "Committed $commit to $repo $staging, on top of $live ($head)."
echo "Compare: https://github.com/$repo/compare/$head...$commit" | tee -a "$summary"
echo "commit=$commit" >> "$out"
