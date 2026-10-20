---
authors: [Theauxm]
repos: [docs, samples, website]
areas: [docs, packaging]
status: accepted
---

# Agent skills are written in the docs and published from a release

Coding agents working in an app built on Trax get skills: short instructions an agent loads when a task matches,
saying what to decide first, which page to read and which mistakes are known. They are written in
`Trax.Docs/skills/<name>/SKILL.md`, and each release publishes them to the `skills` branch of this repository,
stamped with the release's version. Nothing serves them from `main`.

## Status

**Accepted.**

## Decisions

- **Written beside the docs, held to the docs' rules.** A skill routes to pages and holds what the pages do not:
  ordering, decisions and traps. Its C# fences carry the same `compile`, `from=` and `fragment` markers as a page and
  are checked the same way, so a skill that teaches an API the code no longer has fails the build. `skills/` is not
  published as pages (`sync-docs.sh` skips it).
- **Every source skill is marked `metadata.internal: true`.** The skills CLI reads any public repository, and
  `npx skills add TraxSharp/Trax` must not offer what `main` holds before a release ships it. Measured with skills
  1.7.4 (2026-10-20): with only the contributor skill in `.claude/skills/` marked internal, the CLI finds no skill in
  its usual places, falls back to a recursive search and offers everything under `Trax.Docs/skills/`; with every
  skill marked, it offers nothing, `--full-depth` included. Claude Code ignores the key.
- **The release publishes them to the `skills` branch.** One commit per release, tagged `skills-v<version>` (`v*`
  is the packages' tag), with the marker removed, `metadata.trax-version` stamped in and the Claude Code plugin
  manifest (`trax`) at that version. A static marketplace (`traxsharp`) at `.claude-plugin/marketplace.json` on `main`
  names the plugin with the branch as its source, so Claude Code installs with
  `/plugin marketplace add TraxSharp/Trax` and `/plugin install trax@traxsharp`, and updates follow releases. Other
  agents install the branch with `npx skills add https://github.com/TraxSharp/Trax/tree/skills`; the skills CLI does
  not follow a marketplace to another ref (measured). An install from a `skills-v<version>` tree URL records the ref
  in `skills-lock.json` and stays on it, which is how an app on an older Trax keeps matching skills.
- **Only the release writes the branch.** A skill is instructions an agent follows in a consumer's repository, so
  write access to the branch is a way to instruct every consumer's agent. The `skills` ruleset refuses updates,
  deletion and force pushes to anyone but GitHub Actions, so no person pushes there, and of the workflows only the
  release's `skills` job, behind the `release` environment, writes it. The other jobs whose token could are the
  release's own versioning job (tags and the GitHub release) and the Dependabot lockfile heal (`dependabot/**`). The `skills tags` ruleset makes a
  `skills-v*` tag permanent. The job commits through the API, so GitHub signs each commit without a key in the job.
  A skill ships markdown only, never a script.

## Considered options

**A separate repository, `TraxSharp/skills`, written by a GitHub App.** The first version of this decision. It gives
non-Claude agents the shortest install (`npx skills add TraxSharp/skills`) and keeps generated history out of this
repository. Rejected once the internal marker turned out to be needed anyway: with it, `main` serves nothing, so the
second repository only bought a shorter URL, at the cost of an App, its private key and a repository to keep up. The
release job already writes this repository's tags, so the branch adds no new holder of write access.

**`skills/` at the root of `main`.** The CLI's default place. Rejected: the obvious command would install from `main`,
ahead of what is on nuget.org.

**A Claude Code plugin only.** Rejected: people building on Trax use other agents too, and the skills CLI installs
into most of them from the same files.

**Copies in every template.** Version-matched for free, but `npx skills update` never refreshes a copy, so every
project would drift from its own packages after the first bump. Templates point at the published skills instead.

## Exemplars

- `SkillsAreWellFormedTests` holds every skill to the release's rules (name, description, the internal marker, no
  stamped version, markdown only, links inside the skill), every docs link in a skill to a published page, and the
  marketplace on `main` to the one the release expects.
- `Trax.Docs/tools/Trax.Skills.Publish` is the stage the release runs; it applies the same rules and refuses to
  write a tree that breaks one. Its tests are in `StagerTests.cs` and `SkillRulesTests.cs`.
- `CompiledSnippetsTests.cs` and `EveryCSharpFenceIsMarkedTests.cs` in Trax.Docs.Snippets.Tests cover a skill's
  fences as they cover a page's.

Not covered: nothing checks a skill's advice is right, only that its code builds and its links land, and nothing here
checks that only release jobs write the `skills` branch; that is its ruleset.

## Changelog

- **2026-10-20**: Recorded.
