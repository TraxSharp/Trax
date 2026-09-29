---
authors: [Theauxm]
areas: [platform]
status: accepted
---

# A service train instance is one run at a time, and is never a singleton

A `ServiceTrain` instance holds the state of the run in progress: its metadata row, its effect
runner and that runner's data context, and the lifecycle hooks built for the run. So every `Run`
without a caller-supplied row starts a fresh row (and a fresh `ExternalId` unless one was set for
it), `AddSingletonTraxRoute` refuses a service train at registration, and lifecycle hooks are built
from the scope the train was resolved in rather than from the root container.

## Status

**Accepted.**

## Why this is written down

Because each half looks optional on its own. `Run` used to initialize metadata only when it had
none, which reads like a harmless guard; on a second `Run` of the same instance it wrote the
second outcome over the first run's terminal row, so a failure vanished from the record, the
dashboard, the dead-letter count and retries. `AddSingletonTraxRoute` is a documented public
method, and nothing in its signature says a train cannot use it; a singleton train shared one row
and one `DbContext` between every run in the process. And the hook factory is a singleton, so
building hooks from its own provider looks natural; it gave every run the root container's
`IDataContext`, or failed every train resolution when the container validates scopes.

## Considered options

**Refuse a second `Run` on the same instance.** A scoped train resolved once in a request and run
twice is ordinary code, and sequential reuse is safe once each run gets its own row. Refusing it
would break callers for a case the reset handles correctly. Rejected.

**Obsolete or remove `AddSingletonTraxRoute`.** Junctions and other routes that carry no per-run
state register through it (`AddSingletonTraxJunction` delegates to it), so the method stays and
refuses only service trains.

**Make a singleton train work by moving per-run state out of the instance.** `Metadata`,
`EffectRunner` and the other per-run members are public instance properties that hooks, junction
providers and consumers read. Moving them is a breaking redesign for a lifetime nobody needs.

## Consequences

**`TrainLifetime(ServiceLifetime.Singleton)` in the mediator now fails at startup**, because the
mediator registers discovered trains through `AddSingletonTraxRoute`. That is the intent: the
failure used to be silent data loss at run time.

**Concurrent runs of one instance are still wrong and still unchecked.** The reset covers
sequential reuse. Two overlapping `Run` calls on one scoped instance share state, and nothing
detects it. The documentation says an instance is one run at a time.

**A custom `ITrainLifecycleHookFactory` gets the run's scope only if it asks for it.** The runner
calls `Create(IServiceProvider)` with the run's scope; its default calls `Create()`, so a factory
written earlier keeps its old behaviour until it overrides the new overload.

## Exemplars

- `TrainInstanceReuseTests` pins that a scoped train run twice records two rows, keeps an
  `ExternalId` set for the second run, and that a service train cannot be registered as a
  singleton.
- `LifecycleHookScopeTests` pins that a hook's scoped dependency is the run's, not the root's,
  and that a container validating scopes runs the train.
- [Train Registration](/docs/cross-cutting/di-registration/train-registration) is the rule this
  produces.

Not covered: nothing detects two concurrent `Run` calls on one instance, and a custom hook
factory that ignores the provider it is handed still builds from wherever it likes.

## Changelog

- **2026-09-28**: Recorded.
