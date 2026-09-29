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
central pin of every package that template references and nothing else. The root file stays
the only place a version is written.

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
- The `PackTemplatePackageVersions` target in `templates/Trax.Samples.Templates.csproj` fails
  the pack when a template references a package with no central pin.

Not covered: the test restores the scaffolds but does not build or run them; building the same
template projects inside the repo is what the environment tests of samples/0003 do.

## Changelog

- **2026-09-27**: Recorded.
