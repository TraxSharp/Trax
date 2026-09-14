---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples, docs]
areas: [testing, platform]
status: accepted
---

# An ADR's exemplars are declared by attribute, not matched by name

A test an ADR names as an exemplar carries `[ArchitectureGuard("NNNN-slug.md")]`. The guard
resolves a claim by reading that attribute rather than by matching a backticked class name
against every class under `tests/`. The attribute lives in `Trax.Core.Testing`, which every
repo is downstream of.

## Status

**Accepted.** Not yet implemented; see Exemplars.

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

**Put the attribute in each repo rather than in a package.** Rejected. A twelve-line attribute
duplicated nine times is another `SourceText.cs`, and reconciling that file's five divergent
copies is what prompted this audit.

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
not enforce the decision is still possible, and this is exactly the defect this session found
twice: `HostTrackingIntegrationTests` credited to Trax.Effect `0002` while asserting only host
attribution, and a citation on a Trax.Scheduler class that constructs nothing. The attribute
makes the link unambiguous, not honest.

**Cross-repo claims are unaffected.** A central ADR still cannot name a class in another repo,
because the guard reads one checkout. `**Enforced elsewhere:**` remains the form for those.

## Exemplars

**Unenforced:** this decision is carried out by the resolver inside `Trax.Adr.Guard`, not
asserted by a guard. Once `ExemplarGuards` reads the attribute the decision holds by
construction, and a check that claims resolve by attribute would only restate the
implementation. What a guard could usefully add, once the attribute exists, is the converse:
that every class an ADR names carries one. The name matching described above is what runs
today, and its ambiguity in Trax.Scheduler is live while it does.

## Changelog

- **2026-09-14**: Accepted, narrowed to the tests an ADR names. Tagging the whole guard corpus
  is split out as the follow-on decision.
- **2026-09-14**: Proposed, from a workspace-wide audit of which tests are semantically guards.
