---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples, docs]
areas: [testing, platform]
status: proposed
---

# A guard declares itself with an attribute, rather than by where it lives

Membership of the guard corpus is a directory today: the census reads one `Tests.Meta`
project per repo. The proposal is to make membership explicit instead. A guard carries
`[ArchitectureGuard]`, the census scans `tests/` and `src/` for that attribute, and
`--census-root` goes away.

## Status

**Proposed.** Nothing implements this yet.

## Why this is being raised

An audit of all 5,877 tests in the workspace found the directory boundary leaks in both
directions, and that the two halves of the ADR system already disagree about where the corpus
is. `ExemplarGuards` resolves a named class anywhere under `tests/`; `CensusGuards` reads one
project. So an ADR can credit a guard the census will never audit, which is how sixteen
classes came to carry the exact `Enforces <c>adr/NNNN-....md</c>` docstring the census accepts
while sitting outside every census root.

The leaks are concrete. Ten classes assert a property of the codebase and are censused
nowhere, with sixteen more mixing one such assertion into an otherwise behavioural class.
`BookwormDataLayerGuards`, `BookwormCrossSchemaGuards` and `BookwormTrainGuards` escape twice
over, being both outside the root and outside the `*Tests` name pattern the scanner and the
exemplar matcher both require, so `samples/0002` cannot name them even though it wants to. The
five shipped fixtures declare thirteen `[Test]` methods in `src/`, where no test-directory
boundary reaches them at all. And Trax.Effect's `0001` writes ``SqliteMigrationTests.cs`` with
the extension specifically so the exemplar matcher reads it as prose, which is a workaround for
this boundary rather than a use of it.

## Considered options

**Widen `--census-root` to `tests/`.** Rejected. It sweeps roughly 5,800 behaviour tests into
a census that demands each one cite an ADR or write a prose opt-out. The result is thousands of
identical opt-outs and a check nobody reads. Trax.Docs' own workflow already anticipated this
for `Trax.Adr.Guard.Tests` and excluded it for exactly that reason.

**Accept a list of census roots.** `CensusRoot` becomes a list and each repo names its extra
projects. Cheaper, and it closes the out-of-root half. It does not close the name-pattern half,
does not reach `src/`, and leaves every new test project a decision someone has to remember.

**Widen the name pattern to `(Tests|Guards)`.** A one-character-class change in two regexes.
Closes the Bookworm naming half and nothing else, and makes the corpus depend on what people
call things.

**Keep the directory boundary and accept the gap.** Defensible while the guards are
concentrated in `Tests.Meta`, which is where 93 of them are. The cost is that the number the
census reports is not the number of guards, and the audit showed the difference is about a
quarter.

## Consequences if accepted

**A guard says what it is.** Membership stops depending on a path, a project name and a class
suffix agreeing, and the two halves of the system can finally read the same population.

**The `src/`-resident fixture checks enter the census** for the first time, which is the only
route that reaches them.

**Every existing guard needs the attribute**, across 93 classes in eight repos, plus the
scanner rewrite. It is a mechanical change but not a small one, and the census is dark for the
duration.

**A new failure mode replaces the old one.** Forgetting the attribute is silent in a way that
putting a file in the wrong directory is not, because the directory is at least visible in a
diff. The mitigation is the same shape as the census itself: a guard that fails when a class
looks like a guard, by the heuristics the audit used, and does not declare itself.

## Exemplars

**Unenforced:** this is a proposal, so nothing checks it. The audit that motivates it is
recorded rather than automated, and the figures above are a snapshot taken on 2026-09-14 that
will drift until something enforces them.

## Changelog

- **2026-09-14**: Proposed, from a workspace-wide audit of which tests are semantically guards.
