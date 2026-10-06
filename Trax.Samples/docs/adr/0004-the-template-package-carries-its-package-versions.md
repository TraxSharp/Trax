---
authors: [Theauxm]
areas: [samples]
status: accepted
---

# The template package carries its package versions, generated at pack from the central pins

The template projects under `templates/content/` reference packages with no `Version`, because
in this repo Central Package Management supplies them from the root `Directory.Packages.props`.
A project scaffolded from the published `Trax.Samples.Templates` never sees that file, so it
failed to restore with `NU1015`, and `trax generate`, which scaffolds from `trax-hub`, failed
with it. At pack time each template now gets its own `Directory.Packages.props`, holding the
central pin of every package that template's projects (the application and its `tests/`
project) reference and nothing else. The root file stays the only place a version is written.

Pinning the direct references alone was not enough. A Trax package that arrives transitively
resolved at whatever version the package above it was built against: a scaffolded `trax-hub`
pulled the `Trax.Api` packages at the version `Trax.Dashboard` named, older than the pins and
deprecated on nuget.org. The generated file now also pins every Trax package the template's
committed `packages.lock.json` resolves, and turns on `CentralPackageTransitivePinningEnabled`
so those pins reach transitive packages. The lockfile supplies only which packages; the
versions still come from the root pins, so a Trax package a template resolves must be pinned
there.

## Status

**Accepted.**

## Considered options

**A committed `Directory.Packages.props` beside each template.** Visible and simple, but inside
the repo it would shadow the root file for the template projects, so every pin bump would have
to be made twice, and Dependabot would bump the copies on their own schedule. The first missed
edit ships a template whose versions nobody built or tested.

**Versions written into the template `.csproj` files.** Breaks Central Package Management in
the repo (`NU1008`), and has the same second-copy problem as the option above.

**Rewrite each `.csproj` at pack, inserting `Version` attributes.** Works, but the scaffolded
project then differs in shape from the one this repo builds, and a consumer who adds a package
gets no central file to add it to.

**Floating versions (`1.*`).** A scaffold would restore whatever is newest on the day it is
made, including a Trax release this repo has never built the template against.

## Consequences

The packed template ships exactly the versions the repo pinned when it was packed. A scaffold
from an older `Trax.Samples.Templates` keeps its older versions until its owner moves them, which
is ordinary for a project that owns its `Directory.Packages.props`.

The templates' `packages.lock.json` files are no longer packed. They record this repo's restore
of the template projects and stay here for `--locked-mode`; a scaffold writes its own.

Packing from a workspace checkout where `trax-local.props` sets `TraxLocalVersion` bakes that
local version in. Pass `-p:TraxLocalVersion=` to pack the committed pins, as the test does.

## Exemplars

- `ScaffoldedTemplateRestoreTests` packs the templates, installs the package into a private
  template hive, scaffolds each of `trax-hub`, `trax-scheduler` and `trax-api` into a temporary
  directory outside the repo, and restores it.
- `ScaffoldedTemplateRestoreTests.A_scaffolded_project_resolves_only_the_pinned_Trax_versions`
  checks each scaffold's restore resolves every Trax package at exactly the version its
  generated file pins, with transitive pinning on.
- The `PackTemplatePackageVersions` target in `templates/Trax.Samples.Templates.csproj` fails
  the pack when a template references, or its lockfile resolves, a Trax package with no
  central pin.

Not covered here: the test restores the committed pins but does not build or run the scaffolds.
The scaffold tests of samples/0003 build, test and start them, packed with the versions
the repo builds with, which in a workspace checkout is the local feed.

## Changelog

- **2026-10-03**: Amended: each template ships a test project under `tests/`, and the generated
  `Directory.Packages.props` pins its packages too.
- **2026-10-02**: The generated file also pins every Trax package the template resolves
  transitively, with transitive pinning on.
- **2026-09-27**: Recorded.
