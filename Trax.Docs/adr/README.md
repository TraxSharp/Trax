# Decisions

Why a thing is the way it is, which alternatives were weighed, and what each cost. A
documentation page tells you what the rule *is*; an ADR tells you whether it is a
deliberate constraint or an accident, so you can tell which ones are safe to change.

Read the relevant one before proposing to change a rule. If your work contradicts one, say
so rather than silently overriding it.

## Where they live

**This directory holds decisions that bind more than one repo.** A decision governing a
single repo lives in that repo, at `<Repo>/docs/adr/`, because its location is what routes
it to the people it binds and they find it without coming through here.

Numbering is per directory, so a bare number is ambiguous across them. Cite a repo-scoped
ADR by repo, as `effect/0001`.

These pages are **not published** to traxsharp.net. They are engineering record, not
product documentation, and `sync-docs.sh` skips this directory.

## How they are checked

`tools/Trax.Adr.Guard` validates frontmatter, the index tables, the lifecycle sections, the
exemplars, the prose hygiene, and the census of guards against decisions. It runs against this corpus in Trax.Docs CI, and is delivered to the other
repos as the `adr-guard` composite action. Every code repo calls it and carries its own
`docs/adr/`. The format is
[`.claude/skills/recording-decisions/ADR-FORMAT.md`](../.claude/skills/recording-decisions/ADR-FORMAT.md).

Every ADR carries `## Exemplars` in one of three states: guard classes in the same repo
(checked), `**Enforced elsewhere:**` naming guards in another repo (recorded, not checked),
or `**Unenforced:**` with a reason. Decisions in *this* corpus are almost always the middle
one, because a rule binding eight repos is held up by guards in those repos and no checkout
here can see them.

## Two ways to find one

**`repos` says who must obey.** The repos whose developers could break the decision without
realising. A repo-scoped ADR omits the key, because its path already says it.

**`areas` says what it is about.** The subject you would go looking under.

## By repo

| Repo | ADRs |
| --- | --- |
| `api` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md), [0008](./0008-documentation-conventions-are-linted.md), [0009](./0009-feature-tables-ship-in-the-core-provider-set.md), [0010](./0010-the-public-api-surface-is-a-committed-baseline.md), [0011](./0011-test-frameworks-stay-out-of-shipped-libraries.md), [0012](./0012-an-adrs-exemplars-are-declared-by-attribute.md), [0013](./0013-trax-owns-the-vocabulary-for-its-own-concepts.md), [0014](./0014-a-test-owns-every-timeout-it-waits-behind.md), [0015](./0015-every-packageversion-names-a-referenced-package.md), [0016](./0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md), [0017](./0017-a-callers-enqueue-goes-through-the-mediator.md), [0018](./0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md), [0019](./0019-queued-work-for-one-subject-runs-one-at-a-time.md), [0020](./0020-a-failure-is-classified-where-it-happens-and-carried.md) |
| `cli` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0008](./0008-documentation-conventions-are-linted.md), [0010](./0010-the-public-api-surface-is-a-committed-baseline.md), [0011](./0011-test-frameworks-stay-out-of-shipped-libraries.md), [0012](./0012-an-adrs-exemplars-are-declared-by-attribute.md), [0013](./0013-trax-owns-the-vocabulary-for-its-own-concepts.md), [0014](./0014-a-test-owns-every-timeout-it-waits-behind.md), [0015](./0015-every-packageversion-names-a-referenced-package.md), [0016](./0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md) |
| `core` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0008](./0008-documentation-conventions-are-linted.md), [0010](./0010-the-public-api-surface-is-a-committed-baseline.md), [0011](./0011-test-frameworks-stay-out-of-shipped-libraries.md), [0012](./0012-an-adrs-exemplars-are-declared-by-attribute.md), [0013](./0013-trax-owns-the-vocabulary-for-its-own-concepts.md), [0014](./0014-a-test-owns-every-timeout-it-waits-behind.md), [0015](./0015-every-packageversion-names-a-referenced-package.md), [0016](./0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md), [0020](./0020-a-failure-is-classified-where-it-happens-and-carried.md) |
| `dashboard` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md), [0008](./0008-documentation-conventions-are-linted.md), [0010](./0010-the-public-api-surface-is-a-committed-baseline.md), [0011](./0011-test-frameworks-stay-out-of-shipped-libraries.md), [0012](./0012-an-adrs-exemplars-are-declared-by-attribute.md), [0013](./0013-trax-owns-the-vocabulary-for-its-own-concepts.md), [0014](./0014-a-test-owns-every-timeout-it-waits-behind.md), [0015](./0015-every-packageversion-names-a-referenced-package.md), [0016](./0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md), [0017](./0017-a-callers-enqueue-goes-through-the-mediator.md), [0018](./0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md), [0019](./0019-queued-work-for-one-subject-runs-one-at-a-time.md), [0020](./0020-a-failure-is-classified-where-it-happens-and-carried.md) |
| `docs` | [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0008](./0008-documentation-conventions-are-linted.md), [0012](./0012-an-adrs-exemplars-are-declared-by-attribute.md) |
| `effect` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md), [0008](./0008-documentation-conventions-are-linted.md), [0009](./0009-feature-tables-ship-in-the-core-provider-set.md), [0010](./0010-the-public-api-surface-is-a-committed-baseline.md), [0011](./0011-test-frameworks-stay-out-of-shipped-libraries.md), [0012](./0012-an-adrs-exemplars-are-declared-by-attribute.md), [0013](./0013-trax-owns-the-vocabulary-for-its-own-concepts.md), [0014](./0014-a-test-owns-every-timeout-it-waits-behind.md), [0015](./0015-every-packageversion-names-a-referenced-package.md), [0016](./0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md), [0018](./0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md), [0019](./0019-queued-work-for-one-subject-runs-one-at-a-time.md), [0020](./0020-a-failure-is-classified-where-it-happens-and-carried.md) |
| `mediator` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md), [0008](./0008-documentation-conventions-are-linted.md), [0010](./0010-the-public-api-surface-is-a-committed-baseline.md), [0011](./0011-test-frameworks-stay-out-of-shipped-libraries.md), [0012](./0012-an-adrs-exemplars-are-declared-by-attribute.md), [0013](./0013-trax-owns-the-vocabulary-for-its-own-concepts.md), [0014](./0014-a-test-owns-every-timeout-it-waits-behind.md), [0015](./0015-every-packageversion-names-a-referenced-package.md), [0016](./0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md), [0017](./0017-a-callers-enqueue-goes-through-the-mediator.md), [0018](./0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md), [0019](./0019-queued-work-for-one-subject-runs-one-at-a-time.md) |
| `samples` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md), [0008](./0008-documentation-conventions-are-linted.md), [0011](./0011-test-frameworks-stay-out-of-shipped-libraries.md), [0012](./0012-an-adrs-exemplars-are-declared-by-attribute.md), [0013](./0013-trax-owns-the-vocabulary-for-its-own-concepts.md), [0014](./0014-a-test-owns-every-timeout-it-waits-behind.md), [0015](./0015-every-packageversion-names-a-referenced-package.md), [0016](./0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md) |
| `scheduler` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md), [0008](./0008-documentation-conventions-are-linted.md), [0010](./0010-the-public-api-surface-is-a-committed-baseline.md), [0011](./0011-test-frameworks-stay-out-of-shipped-libraries.md), [0012](./0012-an-adrs-exemplars-are-declared-by-attribute.md), [0013](./0013-trax-owns-the-vocabulary-for-its-own-concepts.md), [0014](./0014-a-test-owns-every-timeout-it-waits-behind.md), [0015](./0015-every-packageversion-names-a-referenced-package.md), [0016](./0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md), [0017](./0017-a-callers-enqueue-goes-through-the-mediator.md), [0018](./0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md), [0019](./0019-queued-work-for-one-subject-runs-one-at-a-time.md), [0020](./0020-a-failure-is-classified-where-it-happens-and-carried.md) |
## By area

| Area | ADRs |
| --- | --- |
| `ci` | [0002](./0002-cross-repo-dependencies-are-exact-pinned.md) |
| `data-model` | [0018](./0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md), [0019](./0019-queued-work-for-one-subject-runs-one-at-a-time.md), [0020](./0020-a-failure-is-classified-where-it-happens-and-carried.md) |
| `docs` | [0008](./0008-documentation-conventions-are-linted.md) |
| `graphql` | [0017](./0017-a-callers-enqueue-goes-through-the-mediator.md) |
| `migrations` | [0009](./0009-feature-tables-ship-in-the-core-provider-set.md) |
| `naming` | [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md), [0013](./0013-trax-owns-the-vocabulary-for-its-own-concepts.md) |
| `packaging` | [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0010](./0010-the-public-api-surface-is-a-committed-baseline.md), [0011](./0011-test-frameworks-stay-out-of-shipped-libraries.md), [0015](./0015-every-packageversion-names-a-referenced-package.md) |
| `platform` | [0001](./0001-architectural-rules-are-executable-guards.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md), [0012](./0012-an-adrs-exemplars-are-declared-by-attribute.md), [0013](./0013-trax-owns-the-vocabulary-for-its-own-concepts.md), [0016](./0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md), [0017](./0017-a-callers-enqueue-goes-through-the-mediator.md), [0018](./0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md), [0019](./0019-queued-work-for-one-subject-runs-one-at-a-time.md), [0020](./0020-a-failure-is-classified-where-it-happens-and-carried.md) |
| `providers` | [0009](./0009-feature-tables-ship-in-the-core-provider-set.md) |
| `testing` | [0001](./0001-architectural-rules-are-executable-guards.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0008](./0008-documentation-conventions-are-linted.md), [0010](./0010-the-public-api-surface-is-a-committed-baseline.md), [0011](./0011-test-frameworks-stay-out-of-shipped-libraries.md), [0012](./0012-an-adrs-exemplars-are-declared-by-attribute.md), [0014](./0014-a-test-owns-every-timeout-it-waits-behind.md), [0015](./0015-every-packageversion-names-a-referenced-package.md), [0016](./0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md) |

## All of them

| # | Decision | Repos | Areas |
| --- | --- | --- | --- |
| [0001](./0001-architectural-rules-are-executable-guards.md) | Architectural rules are executable guards, not prose | core, effect, mediator, scheduler, dashboard, api, cli, samples | testing, platform |
| [0002](./0002-cross-repo-dependencies-are-exact-pinned.md) | Cross-repo dependencies are exact-pinned and lockfiled | core, effect, mediator, scheduler, dashboard, api, cli, samples | packaging, ci |
| [0003](./0003-a-repo-depends-only-on-what-is-upstream.md) | A repo depends only on what is upstream of it | core, effect, mediator, scheduler, dashboard, api, cli, samples | packaging, platform |
| [0004](./0004-tests-assert-with-fluentassertions.md) | Tests assert with FluentAssertions | core, effect, mediator, scheduler, dashboard, api, cli, samples, docs | testing |
| [0005](./0005-a-skipped-test-is-a-runtime-decision.md) | A skipped test is a runtime decision, never an attribute | core, effect, mediator, scheduler, dashboard, api, cli, samples, docs | testing |
| [0006](./0006-tests-synchronise-on-a-signal.md) | Tests synchronise on a signal, never on a fixed delay | core, effect, mediator, scheduler, dashboard, api, cli, samples | testing |
| [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md) | The canonical train name is the interface FullName | effect, mediator, scheduler, dashboard, api, samples | naming, platform |
| [0008](./0008-documentation-conventions-are-linted.md) | Documentation conventions are enforced by lint, not by review | core, effect, mediator, scheduler, dashboard, api, cli, samples, docs | docs, testing |
| [0009](./0009-feature-tables-ship-in-the-core-provider-set.md) | Feature-package tables ship in the core provider migration set | effect, api | migrations, providers |
| [0010](./0010-the-public-api-surface-is-a-committed-baseline.md) | The public API surface is a committed baseline | core, effect, mediator, scheduler, dashboard, api, cli | packaging, testing |
| [0011](./0011-test-frameworks-stay-out-of-shipped-libraries.md) | Test frameworks stay out of shipped libraries, except where fixtures are the product | core, effect, mediator, scheduler, dashboard, api, cli, samples | testing, packaging |
| [0012](./0012-an-adrs-exemplars-are-declared-by-attribute.md) | An ADR's exemplars are declared by attribute, not matched by name | core, effect, mediator, scheduler, dashboard, api, cli, samples, docs | testing, platform |
| [0013](./0013-trax-owns-the-vocabulary-for-its-own-concepts.md) | Trax owns the vocabulary for its own concepts | core, effect, mediator, scheduler, dashboard, api, cli, samples | platform, naming |
| [0014](./0014-a-test-owns-every-timeout-it-waits-behind.md) | A test owns every timeout it waits behind | core, effect, mediator, scheduler, dashboard, api, cli, samples | testing |
| [0015](./0015-every-packageversion-names-a-referenced-package.md) | Every PackageVersion names a package a project references | core, effect, mediator, scheduler, dashboard, api, cli, samples | packaging, testing |
| [0016](./0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md) | A junction chain is a declaration, not a step of the work | core, effect, mediator, scheduler, api, dashboard, cli, samples | platform, testing |
| [0017](./0017-a-callers-enqueue-goes-through-the-mediator.md) | A caller's enqueue goes through the mediator | mediator, scheduler, api, dashboard | platform, graphql |
| [0018](./0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md) | A deferred enqueue is staged, and a stranded one is cancelled | effect, mediator, scheduler, api, dashboard | platform, data-model |
| [0019](./0019-queued-work-for-one-subject-runs-one-at-a-time.md) | Queued work for one subject runs one at a time | effect, mediator, scheduler, api, dashboard | platform, data-model |
| [0020](./0020-a-failure-is-classified-where-it-happens-and-carried.md) | A failure is classified where it happens, and the answer is carried | core, effect, scheduler, api, dashboard | platform, data-model |
