---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples, docs]
areas: [testing, platform]
status: accepted
---

# An ADR's exemplars are declared by attribute, not matched by name

A test an ADR names as an exemplar carries `[Property("adr", "<path>/NNNN-slug.md")]`. The
guard resolves a claim by reading that attribute rather than by matching a backticked class
name against every class under `tests/`. The marker is NUnit's own property attribute, which
every test project in the workspace already has.

## Status

**Accepted.**

## Why this is written down

Because name matching has a hole that is already live. `ExemplarGuards.Claims` matches a bare
backticked name against every class in the test tree, and Trax.Scheduler declares two classes
called `HttpRunExecutorTests`, in different projects. One pins the outgoing request shape; the
other covers response and error mapping. `scheduler/0001` claims the name, so the claim
resolves against whichever the scanner reaches first, and **deleting the one that does the work
the ADR describes leaves the ADR green.**

The same weakness is admitted in the guard's own source: checking that a name appeared
somewhere was "too weak to mean anything", because a stray comment or an unrelated string
literal satisfied it. The two-place citation rule exists to patch that, and it patches the
symptom. An attribute cannot be satisfied by prose, survives a rename, and identifies one
declaration rather than a name that may have several.

Scope, measured on 2026-09-14: 21 ADRs name 31 distinct classes, resolving to **91 class
declarations**, 76 inside a census root and 15 outside it.

## Considered options

**Keep matching by name and rely on the two-place citation.** The status quo. It is cheap and
it is what the corpus does today, but it cannot distinguish two classes sharing a name, and the
citation it requires can be satisfied by text that enforces nothing. Trax.Effect's `0001`
already works around the matcher from the other side, writing ``SqliteMigrationTests.cs`` with
the extension so the name reads as prose.

**Tag every guard in the workspace, not just the named ones.** The original form of this
proposal. It would grow the corpus rather than harden the link: roughly 119 declarations
against 91, so only about a quarter more work, and it is the only route that reaches the
thirteen checks living on fixtures in `src/`. Deferred rather than rejected, because it is a
different decision: this one makes an existing link trustworthy, that one decides what belongs
in the corpus at all. It should follow, not lead.

**A dedicated `[ArchitectureGuard]` attribute shipped in `Trax.Core.Testing`.** The first form
of this decision, rejected on cost once it was implemented. No repo pins `Trax.Core.Testing`
today, so it meant a new central pin and a `PackageReference` in eight `Tests.Meta` projects,
and Trax.Docs has no `Directory.Packages.props` at all, so a documentation repo would have
acquired package management and a dependency on Trax.Core to hold a twelve-line attribute.
NUnit's `[Property]` is already present everywhere and is designed for exactly this.

**Put a dedicated attribute in each repo rather than in a package.** Rejected. A twelve-line
attribute duplicated nine times is another `SourceText.cs`, and reconciling that file's five
divergent copies is what prompted this audit.

## Consequences

**Eight `Tests.Meta` projects take a dependency on `Trax.Core.Testing`.** None references any
`Trax.*.Testing` package today, so this is new coupling, though it runs with the dependency
chain rather than against it. It is also the first use of those packages from the Meta
projects, which is worth noting against the finding that forty censused classes hand-roll five
checks the packages already ship.

**The failure-message citation stays.** The attribute replaces the docstring half of the
citation and the name matching, not the message. Whoever trips a guard has to see the ADR at
the point of failure, and an attribute never appears in output.

**A claim can still be wrong, only differently.** Attaching the attribute to a test that does
not enforce the decision remains possible, and that defect has already occurred twice in this
corpus: `HostTrackingIntegrationTests` was credited to Trax.Effect `0002` while asserting only
host attribution, and a Trax.Scheduler class carried a citation describing a record it never
constructs. The attribute makes the link unambiguous, not honest.

**Cross-repo claims are unaffected.** A central ADR still cannot name a class in another repo,
because the guard reads one checkout. `**Enforced elsewhere:**` remains the form for those.

## Exemplars

**Enforced elsewhere:** `ExemplarGuards.NamedGuardsResolve` in this repo's
`tools/Trax.Adr.Guard` resolves every claim through the attribute and fails three ways: the
named class does not exist, it exists but claims no ADR back, or more than one class claims the
same ADR. `NamedGuardsCiteBack` still requires the ADR in a failure message, and skips the
attribute line when looking, so tagging a class cannot satisfy the half a reader actually sees.

Not covered: nothing checks that a tagged class enforces the decision it names. Resolution
proves a class answers to the ADR, not that its assertions have anything to do with it, and
the two miscredited classes named under Consequences were both unambiguous and wrong.

## Changelog

- **2026-09-14**: Implemented. 90 declarations tagged, the resolver reads the attribute, and
  the marker is NUnit's `[Property]` rather than a dedicated attribute in a package, because
  the package home cost eight new pins and a first dependency for Trax.Docs.
- **2026-09-14**: Accepted, narrowed to the tests an ADR names. Tagging the whole guard corpus
  is split out as the follow-on decision.
- **2026-09-14**: Proposed, from a workspace-wide audit of which tests are semantically guards.
