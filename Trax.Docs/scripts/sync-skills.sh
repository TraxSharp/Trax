#!/usr/bin/env bash
set -euo pipefail

# Copies the agent skills Trax.Docs owns into every repo's .claude/skills/.
#
# Trax.Docs owns them. The copies exist because each repo must be usable on its own: a
# contributor who clones only Trax.Effect still needs to know the ADR format.
#
# It manages ONLY the skills present in Trax.Docs, one directory at a time. A repo-local
# skill sitting beside them is left alone, so this cannot silently delete work that Trax.Docs
# does not own.
#
# Usage: ./Trax.Docs/scripts/sync-skills.sh [--check]
#   --check  report drift and exit non-zero, changing nothing

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
SOURCE="$HERE/../.claude/skills"

CHECK=0
case "${1:-}" in
    --check) CHECK=1 ;;
    "") ;;
    *)
        echo "Unknown argument: $1" >&2
        echo "Usage: $0 [--check]" >&2
        exit 2
        ;;
esac

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

# The skills Trax.Docs owns. Anything else in a repo's .claude/skills/ is that repo's own.
owned=()
while IFS= read -r dir; do
    owned+=("$(basename "$dir")")
done < <(find "$SOURCE" -mindepth 1 -maxdepth 1 -type d | sort)

if [[ ${#owned[@]} -eq 0 ]]; then
    echo "No skill directories under $SOURCE" >&2
    exit 1
fi

drift=0

for repo in "${repos[@]}"; do
    [[ -d "$ROOT/$repo/.git" ]] || continue
    target="$ROOT/$repo/.claude/skills"

    if [[ $CHECK -eq 1 ]]; then
        # A repo with none of the owned skills has not adopted yet. That is a rollout state,
        # not drift, and calling it drift would make --check useless until every repo is in.
        if [[ ! -d "$target" ]]; then
            echo "not adopted: $repo"
            continue
        fi

        for skill in "${owned[@]}"; do
            if ! diff -rq "$SOURCE/$skill" "$target/$skill" >/dev/null 2>&1; then
                echo "drift: $repo/$skill"
                diff -rq "$SOURCE/$skill" "$target/$skill" 2>&1 | sed 's/^/    /'
                drift=1
            fi
        done
        continue
    fi

    mkdir -p "$target"
    for skill in "${owned[@]}"; do
        rm -rf "${target:?}/${skill:?}"
        cp -R "$SOURCE/$skill" "$target/$skill"
    done
    echo "synced: $repo (${owned[*]})"
done

if [[ $CHECK -eq 1 ]]; then
    if [[ $drift -eq 0 ]]; then
        echo "Every adopted skill copy matches Trax.Docs."
    fi
    exit $drift
fi
