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
if [[ $# -gt 1 ]]; then
    echo "Too many arguments. Usage: $0 [--check]" >&2
    exit 2
fi
case "${1:-}" in
    --check) CHECK=1 ;;
    "") ;;
    -h | --help)
        echo "Usage: $0 [--check]"
        exit 0
        ;;
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
)

# Trax.Website is deliberately absent: it has no .NET project and no CI workflow, so there is
# nothing there to run the guard and a skill copy would sit unused and unenforced.

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
refused=0
synced=0
checked=0

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

        checked=$((checked + 1))
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
    repo_refused=0
    for skill in "${owned[@]}"; do
        dest="${target:?}/${skill:?}"

        # Never clobber work git cannot get back. An untracked or modified file inside a
        # skill directory is somebody's in-progress edit, and rm -rf would take it silently.
        if [[ -d "$dest" ]]; then
            dirty="$(git -C "$ROOT/$repo" status --porcelain -- ".claude/skills/$skill" 2>/dev/null || true)"
            if [[ -n "$dirty" ]]; then
                echo "REFUSED: $repo/$skill has uncommitted changes" >&2
                echo "$dirty" | sed 's/^/    /' >&2
                echo "    commit or discard them, then run again" >&2
                refused=1
                repo_refused=1
                continue
            fi
        fi

        rm -rf "$dest"
        cp -R "$SOURCE/$skill" "$dest"
    done
    if [[ $repo_refused -eq 0 ]]; then
        echo "synced: $repo (${owned[*]})"
        synced=$((synced + 1))
    fi
done

if [[ $CHECK -eq 1 ]]; then
    # Reporting success over an empty set is the failure this whole workspace guards against
    # elsewhere. If nothing was examined, say so and fail.
    if [[ $checked -eq 0 ]]; then
        echo "No repository was checked. Run this from the workspace root." >&2
        exit 1
    fi
    if [[ $drift -eq 0 ]]; then
        echo "Every adopted skill copy matches Trax.Docs ($checked checked)."
    fi
    exit $drift
fi

if [[ $synced -eq 0 ]]; then
    echo "No repository was synced. Run this from the workspace root." >&2
    exit 1
fi

exit $refused
