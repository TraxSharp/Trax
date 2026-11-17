# Decisions

Why a thing in `Trax.Analyzers` is the way it is, which alternatives were weighed, and what each
cost. A documentation page tells you what the rule *is*; an ADR tells you whether it is a
deliberate constraint or an accident, so you can tell which ones are safe to change.

## Scope

**These bind `Trax.Analyzers` only.** The decision to keep a copy of Meziantou.Analyzer rather
than reference it, and to put the Trax repository under the profile, is central:
`Trax.Docs/adr/0061`.

Numbering is per directory. Cite one of these as `analyzers/0001`.

## How they are checked

The `adr-guard` job in `.github/workflows/ci.yml` runs the guard against this directory on every
pull request that touches this folder. The format is `.claude/skills/recording-decisions/ADR-FORMAT.md`.

## All of them

None yet.
