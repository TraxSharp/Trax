# Trax.Analyzers

The Roslyn rules behind the opt-in `Trax.Analyzers` package, and the profile every Trax folder
builds under (`Trax.Docs/adr/0061`). Read the root `AGENTS.md` first.

## What is copied and what is Trax's

Most of this folder is a copy of [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer)
(MIT), made by `tools/sync-upstream.sh` from the tag `upstream.txt` names: the rules, code fixes,
suppressors and annotations under `src/`, the Roslyn helpers in `src/Trax.Analyzers.Roslyn`, the
tests in `tests/Trax.Analyzers.Tests` and the rule pages in `docs/rules`. The script renames
`Meziantou.Analyzer` to `Trax.Analyzers` and `MA0004` to `TRX0004`, and lists every rename.

- **A fix to a rule goes in the copied file.** The next sync replaces those files, so either send
  the fix upstream too, or note it in `upstream.txt` and reapply it after a sync.
- **Everything else is Trax's own**: the project files and `src/*.props`, the package
  (`src/Trax.Analyzers.Package`), `rules.txt`, the ban files, `tests/Trax.Analyzers.Package.Tests`
  and `tools/`. The polyfills in `src/Trax.Analyzers.Polyfills` are generated once by
  `tools/generate-polyfills.sh`.
- **Moving to a newer upstream tag**: pick one at least 7 days old, run
  `tools/sync-upstream.sh <tag>`, read the diff, and give every new rule a line in `rules.txt`
  (`ProfileTests` fails until you do).

## The profile

`rules.txt` is the one place a rule is turned on, off or limited to source or test projects. After
editing it, run `tools/generate-profile.sh`; `ProfileTests` fails when the generated files under
`src/Trax.Analyzers.Package/configuration/` disagree with it. A change that adds an error to a
consumer's build gets a new `TraxAnalysisLevel` rather than changing level 1.

The copied code and its tests are not under the profile (they keep upstream's style, so a sync is a
readable diff), and `.csharpierignore` keeps the formatter off them.

## Building and testing

`trax-test Trax.Analyzers`. The rules are built twice, against Roslyn 5.0 and 5.6
(`src/Analyzer.props` says why); the tests run the 5.6 build. Some of the copied tests download
reference packages from api.nuget.org while they run, so they fail where the test step has no
network.
