---
authors: [Theauxm]
areas: [tooling]
status: accepted
---

# `generate --force` replaces its output only on success, and never a repository

`trax generate` builds the new project in a hidden sibling of `--output` and moves it into
place only after every step (`dotnet new trax-hub`, the trains library, the project reference,
the `Program.cs` patch) has succeeded. With `--force` the existing directory is swapped out at
that point, not before, so a failed run leaves it exactly as it was. `--force` is refused
outright when the output is the current directory or one of its parents, or holds a `.git`
directory or file at any depth: a directory of side-by-side repositories is refused just as a
repository is.

## Status

**Accepted.**

## Considered options

**Delete, then generate**, which is what `--force` did until 2026-09-27. Rejected because the
step most likely to fail, `dotnet new` with the template not installed, ran after the delete:
`--output .` in a repository left an empty directory, `.git` included.

**Delete only the files a previous run generated.** Rejected because the generator keeps no
manifest of what it wrote, and the hub is the template's output, which changes between
template versions. Guessing would either leave stale files or delete the user's.

**Prompt for confirmation.** Rejected because `generate` runs in scripts, and a prompt that a
flag skips protects no one who already typed the flag.

**Allow a repository with a stronger flag.** Rejected: regenerating into a working tree is
what `--output` pointing at a fresh directory, followed by a copy, already does, and the
refusal names the reason. Nothing is lost by making that the only way.

## Consequences

A crash between the two renames of the swap can leave the old directory under a
`.<name>.trax-previous-<guid>` sibling rather than at `--output`. Both renames are on one
volume, so that window is two syscalls wide, and the old contents are still on disk under a
name that says what they are.

## Exemplars

- `GenerateForceTests` pins it: a failing scaffold leaves the existing directory and its files,
  leaves no staging directory behind, and creates nothing when the output did not exist; a
  successful run replaces the directory; `.git` (as a directory or a worktree's file, in the
  output or in any directory below it), the current directory and its parents are refused
  before any work.
- [CLI](/docs/reference/cli#options) states what `--force` does.

Not covered: the rollback after a failed second rename is not exercised, because no test can
make a same-volume rename fail on demand.

## Changelog

- **2026-09-29**: A `.git` below the output is refused too, not only one in it.
- **2026-09-27**: Recorded.
