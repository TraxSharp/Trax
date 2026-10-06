---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples, docs]
areas: [packaging, ci]
status: accepted
---

# Trax is one repository, its packages reference each other as projects, and they release at one version

Every Trax package, the samples, the templates and the documentation live in one repository,
`TraxSharp/Trax`, one folder each. Inside it a Trax package is always a `ProjectReference`,
never a `PackageReference`, so every build compiles against the code in the same commit.
Every package releases at one shared version, cut by one release run.

## Status

**Accepted.** Supersedes [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), which
pinned each cross-repo dependency to a published release and needed a local feed, a
`1.99.99` override and an upstream-to-downstream release sequence to make a change that
spanned repos.

## Considered options

**Stay with ten repositories.** What 0002 served. A change crossing repos needed a release of
the upstream half before the downstream half could pass CI, so one fix became a chain of
pull requests and releases, and a fix that was merged but never released reached nobody.
Docs compiled their snippets against the last release, so a page could only describe an API
after it shipped.

**One repository, independent versions per package.** Keeps each package's version
meaningful on its own, but semantic-release has no notion of a path, so it needs a
monorepo plugin or a hand-written commit filter, a tag namespace per package, and a release
order again whenever a change crosses folders. Every package already ships together in
practice, and there is one consumer, so the independence buys nothing to pay for that.

## Consequences

**Versions jump once.** The shared version continues above the highest release any package
had, so the first shared release is 1.61.0 for every package, Trax.Core included. That is a
minor release for each: nothing a consumer compiles against changes. A 2.0.0 was rejected,
because nothing breaks and because unlisted 2.0.0 packages already exist for trax.core and
trax.scheduler.

**The dependency direction still holds**, folder for folder, as
[0003](./0003-a-repo-depends-only-on-what-is-upstream.md) sets it for repos. A
`ProjectReference` makes a wrong-way dependency as easy to add as a right-way one, so the
guard now reads project references as well as package references.

**The templates are the one place a Trax package is a `PackageReference`**, because a
scaffolded project lives outside the repository. In the repository those references are
swapped for project references at build time, and the packed template carries every Trax
package at the version being released.

## Exemplars

**Enforced elsewhere:** `TraxReferencesAreProjectReferencesTests` (no `PackageReference` to a
Trax package outside the template content) and `DependencyDirectionTests` (project and
package references point upstream) in the `Tests.Meta` project of each of the eight code
folders, Trax.Core through Trax.Samples.

Not covered: nothing checks that every package still releases at the shared version. That
is held by there being one release workflow that packs every folder with the same version,
not by a test.

## Changelog

- **2026-10-06**: Recorded.
