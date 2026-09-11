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

`tools/Trax.Adr.Guard` validates frontmatter, the index tables, the lifecycle sections and
the exemplars. It runs against this corpus in Trax.Docs CI, and is delivered to the other
repos as the `adr-guard` composite action. `Trax.Effect` is the first repo to call it and
carries its own `docs/adr/`; the rest adopt in turn. The format is
[`.claude/skills/recording-decisions/ADR-FORMAT.md`](../.claude/skills/recording-decisions/ADR-FORMAT.md).

Every ADR ends with `## Exemplars` in one of three states: guard classes in the same repo
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
| `api` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md), [0008](./0008-documentation-conventions-are-linted.md), [0009](./0009-feature-tables-ship-in-the-core-provider-set.md) |
| `cli` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0008](./0008-documentation-conventions-are-linted.md) |
| `core` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0008](./0008-documentation-conventions-are-linted.md) |
| `dashboard` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md), [0008](./0008-documentation-conventions-are-linted.md) |
| `docs` | [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0008](./0008-documentation-conventions-are-linted.md) |
| `effect` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md), [0008](./0008-documentation-conventions-are-linted.md), [0009](./0009-feature-tables-ship-in-the-core-provider-set.md) |
| `mediator` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md), [0008](./0008-documentation-conventions-are-linted.md) |
| `samples` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0008](./0008-documentation-conventions-are-linted.md) |
| `scheduler` | [0001](./0001-architectural-rules-are-executable-guards.md), [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md), [0008](./0008-documentation-conventions-are-linted.md) |

## By area

| Area | ADRs |
| --- | --- |
| `ci` | [0002](./0002-cross-repo-dependencies-are-exact-pinned.md) |
| `docs` | [0008](./0008-documentation-conventions-are-linted.md) |
| `migrations` | [0009](./0009-feature-tables-ship-in-the-core-provider-set.md) |
| `naming` | [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md) |
| `packaging` | [0002](./0002-cross-repo-dependencies-are-exact-pinned.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md) |
| `platform` | [0001](./0001-architectural-rules-are-executable-guards.md), [0003](./0003-a-repo-depends-only-on-what-is-upstream.md), [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md) |
| `providers` | [0009](./0009-feature-tables-ship-in-the-core-provider-set.md) |
| `testing` | [0001](./0001-architectural-rules-are-executable-guards.md), [0004](./0004-tests-assert-with-fluentassertions.md), [0005](./0005-a-skipped-test-is-a-runtime-decision.md), [0006](./0006-tests-synchronise-on-a-signal.md), [0008](./0008-documentation-conventions-are-linted.md) |

## All of them

| # | Decision | Repos | Areas |
| --- | --- | --- | --- |
| [0001](./0001-architectural-rules-are-executable-guards.md) | Architectural rules are executable guards, not prose | core, effect, mediator, scheduler, dashboard, api, cli, samples | testing, platform |
| [0002](./0002-cross-repo-dependencies-are-exact-pinned.md) | Cross-repo dependencies are exact-pinned and lockfiled | core, effect, mediator, scheduler, dashboard, api, cli, samples | packaging, ci |
| [0003](./0003-a-repo-depends-only-on-what-is-upstream.md) | A repo depends only on what is upstream of it | core, effect, mediator, scheduler, dashboard, api, cli, samples | packaging, platform |
| [0004](./0004-tests-assert-with-fluentassertions.md) | Tests assert with FluentAssertions | core, effect, mediator, scheduler, dashboard, api, cli, samples, docs | testing |
| [0005](./0005-a-skipped-test-is-a-runtime-decision.md) | A skipped test is a runtime decision, never an attribute | core, effect, mediator, scheduler, dashboard, api, cli, samples, docs | testing |
| [0006](./0006-tests-synchronise-on-a-signal.md) | Tests synchronise on a signal, never on a fixed delay | core, effect, mediator, scheduler, dashboard, api, cli, samples | testing |
| [0007](./0007-the-canonical-train-name-is-the-interface-fullname.md) | The canonical train name is the interface FullName | effect, mediator, scheduler, dashboard, api | naming, platform |
| [0008](./0008-documentation-conventions-are-linted.md) | Documentation conventions are enforced by lint, not by review | core, effect, mediator, scheduler, dashboard, api, cli, samples, docs | docs, testing |
| [0009](./0009-feature-tables-ship-in-the-core-provider-set.md) | Feature-package tables ship in the core provider migration set | effect, api | migrations, providers |
