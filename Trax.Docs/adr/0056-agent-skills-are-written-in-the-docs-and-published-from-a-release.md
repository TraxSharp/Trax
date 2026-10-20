---
authors: [Theauxm]
repos: [docs, samples, website]
areas: [docs, packaging]
status: accepted
---

# Agent skills are written in the docs and published from a release

Coding agents working in an app built on Trax get skills: short instructions an agent loads when a task matches,
saying what to decide first, which page to read and which mistakes are known. They are written in
`Trax.Docs/skills/<name>/SKILL.md`, and each release publishes them to their own repository, `TraxSharp/skills`,
stamped with the release's version. Nothing serves them from `main`.

## Status

**Accepted.**

## Decisions

- **Written beside the docs, held to the docs' rules.** A skill routes to pages and holds what the pages do not:
  ordering, decisions and traps. Its C# fences carry the same `compile`, `from=` and `fragment` markers as a page and
  are compiled the same way, so a skill that teaches an API the code no longer has fails the build. `skills/` is not
  published as pages (`sync-docs.sh` skips it).
- **Every source skill is marked `metadata.internal: true`.** The skills CLI reads any public repository, and
  `npx skills add TraxSharp/Trax` must not offer what `main` holds before a release ships it. Measured with skills
  1.7.4 (2026-10-20): with only the contributor skill in `.claude/skills/` marked internal, the CLI finds no skill in
  its usual places, falls back to a recursive search and offers everything under `Trax.Docs/skills/`; with every
  skill marked, it offers nothing, `--full-depth` included. Claude Code ignores the key.
- **The release publishes them to `TraxSharp/skills`.** One commit per release on its `main`, tagged `v<version>`,
  with the marker removed, `metadata.trax-version` stamped in, and a Claude Code plugin (`trax`) and marketplace
  (`traxsharp`) manifest at the root. Its `main` is always the latest release, so the obvious install,
  `npx skills add TraxSharp/skills` or `/plugin marketplace add TraxSharp/skills`, gets released skills, and
  `npx skills update` follows releases. An install from a tag's tree URL records the ref in `skills-lock.json`
  and stays on it, which is how an app on an older Trax keeps matching skills.
- **Only the release writes there.** A skill is instructions an agent follows in a consumer's repository, so write
  access to `TraxSharp/skills` is a way to instruct every consumer's agent. The release job holds the only
  credential, scoped to that repository; the repository's ruleset requires verified signatures and refuses direct
  pushes from people; a skill ships markdown only, never a script.

## Considered options

**`skills/` at the root of `TraxSharp/Trax`.** The CLI's default place, and the simplest. Rejected: the obvious
command would install from `main`, ahead of what is on nuget.org.

**A `skills` branch in `TraxSharp/Trax`.** Rejected: the install needs a tree URL naming the branch, and the obvious
`npx skills add TraxSharp/Trax` still serves `main`.

**A Claude Code plugin only.** Rejected: people building on Trax use other agents too, and the skills CLI installs
into most of them from the same files.

**Copies in every template.** Version-matched for free, but `npx skills update` never refreshes a copy, so every
project would drift from its own packages after the first bump. Templates point at the published skills instead.

## Exemplars

- `SkillsAreWellFormedTests` holds every skill to the release's rules (name, description, the internal marker, no
  stamped version, markdown only, links inside the skill) and every docs link in a skill to a published page.
- `Trax.Docs/tools/Trax.Skills.Publish` is the stage the release runs; it applies the same rules and refuses to
  write a tree that breaks one. Its tests are in `StagerTests.cs` and `SkillRulesTests.cs`.
- `CompiledSnippetsTests.cs` and `EveryCSharpFenceIsMarkedTests.cs` in Trax.Docs.Snippets.Tests cover a skill's
  fences as they cover a page's.

Not covered: nothing checks a skill's advice is right, only that its code builds and its links land, and nothing here
checks that `TraxSharp/skills` takes writes from the release alone; that is its ruleset.

## Changelog

- **2026-10-20**: Recorded.
