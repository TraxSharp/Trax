#!/usr/bin/env bash
set -euo pipefail

# Copies the canonical agent skills from Trax.Docs into every repo's .claude/skills/.
#
# Trax.Docs owns them. The copies exist because each repo must be usable on its own: a
# contributor who clones only Trax.Effect still needs to know the ADR format. The guard
# checks nothing about these copies, so run this after editing the canonical copy.
#
# Usage: ./Trax.Docs/scripts/sync-skills.sh [--check]
#   --check  report drift and exit non-zero, changing nothing

# This script lives in Trax.Docs, which owns the skills, so it is versioned. The repos it
# writes to are its sibling directories in the workspace.
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
SOURCE="$HERE/../.claude/skills"

CHECK=0
if [[ "${1:-}" == "--check" ]]; then
    CHECK=1
fi

if [[ ! -d "$SOURCE" ]]; then
    echo "No skills at $SOURCE" >&2
    exit 1
fi

repos=(
    Trax.Core
    Trax.Effect
    Trax.Mediator
    Trax.Scheduler
    Trax.Dashboard
    Trax.Api
    Trax.Cli
    Trax.Samples
    Trax.Website
)

drift=0

for repo in "${repos[@]}"; do
    target="$ROOT/$repo/.claude/skills"
    [[ -d "$ROOT/$repo/.git" ]] || continue

    if [[ $CHECK -eq 1 ]]; then
        # A repo with no .claude/skills has not adopted yet. That is a rollout state, not
        # drift, and calling it drift would make --check useless until every repo is in.
        if [[ ! -d "$target" ]]; then
            echo "not adopted: $repo"
            continue
        fi

        if ! diff -rq "$SOURCE" "$target" >/dev/null 2>&1; then
            echo "drift: $repo"
            diff -rq "$SOURCE" "$target" 2>&1 | sed 's/^/    /'
            drift=1
        fi
        continue
    fi

    mkdir -p "$target"
    rm -rf "${target:?}"/*
    cp -R "$SOURCE"/. "$target"/
    echo "synced: $repo"
done

if [[ $CHECK -eq 1 ]]; then
    if [[ $drift -eq 0 ]]; then
        echo "Every adopted skill copy matches Trax.Docs."
    fi
    exit $drift
fi
