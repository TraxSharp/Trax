---
title: Trax.Effect.Data.Testing
description: "Reference for Trax.Effect.Data.Testing: the data-layer guards and their NUnit fixture, the owner-scope census options, and the TraxInvariants database checks."
type: reference
area: testing
package: Trax.Effect.Data.Testing
namespace: Trax.Effect.Data.Testing
source:
  - Trax.Effect/src/Trax.Effect.Data.Testing/DataLayerGuards.cs
  - Trax.Effect/src/Trax.Effect.Data.Testing/DomainDataLayerGuardFixture.cs
  - Trax.Effect/src/Trax.Effect.Data.Testing/OwnerScopeCensusOptions.cs
  - Trax.Effect/src/Trax.Effect.Data.Testing/TraxInvariants.cs
parent: Testing
grand_parent: SDK Reference
nav_order: 2
---

# Trax.Effect.Data.Testing

The data-layer guard package: checkers for the shape of domain data contexts, their EF models and their owner scoping, an NUnit fixture that runs them, and `TraxInvariants`, which checks a Trax database left consistent after a test. It depends on [Trax.Core.Testing](/docs/sdk-reference/testing/core-testing), `Trax.Effect.Data`, Roslyn and the Npgsql EF Core provider.

## Signatures

```csharp fragment
namespace Trax.Effect.Data.Testing;

public static class DataLayerGuards
{
    public static GuardResult DomainContextsDeriveBase(ArchitectureGuardOptions options, string baseTypeName = "DomainDataContext", IReadOnlySet<string>? knownExceptions = null);
    public static GuardResult CompanionInterfaces(ArchitectureGuardOptions options, string baseTypeName = "DomainDataContext");
    public static GuardResult OneSchemaPerContext(IReadOnlyList<Type> contextTypes);
    public static GuardResult NoPendingModelChanges(IReadOnlyList<Type> migrationContextTypes);
    public static GuardResult OwnerScopeCompleteness(IReadOnlyModel model, OwnerScopeCensusOptions options);
    public static GuardResult OwnerScopeFilterBypasses(ArchitectureGuardOptions scan, IReadOnlyList<IReadOnlyModel> models, OwnerScopeCensusOptions options);
}

[TestFixture]
public abstract class DomainDataLayerGuardFixture
{
    protected abstract ArchitectureGuardOptions Options { get; }
    protected virtual IReadOnlyList<Type> DomainContexts { get; }
    protected virtual IReadOnlyList<Type> MigrationContexts { get; }
    protected virtual IReadOnlyList<IReadOnlyModel> OwnerScopedModels { get; }
    protected virtual OwnerScopeCensusOptions? OwnerScope { get; }
    protected virtual bool ScanForOwnerScopeFilterBypasses { get; }
    protected virtual string BaseTypeName { get; }

    [Test] public void Domain_contexts_derive_the_shared_base();
    [Test] public void Domain_contexts_have_companion_interfaces();
    [Test] public void Each_domain_context_owns_a_distinct_schema();
    [Test] public void Each_migration_context_matches_its_snapshot();
    [Test] public void Every_per_user_entity_is_scoped_to_its_owner();
    [Test] public void Query_code_does_not_switch_an_owner_scope_filter_off();
}

public sealed record OwnerScopeCensusOptions
{
    public required Type OwnerType { get; init; }
    public required Type PrincipalAccessorType { get; init; }
    public IReadOnlySet<string> OwnerIdProperties { get; init; }
    public IReadOnlyDictionary<Type, string> NavigationScoped { get; init; }
    public IReadOnlyDictionary<Type, string> Exemptions { get; init; }
    public IReadOnlyDictionary<Type, string> Gated { get; init; }
    public IReadOnlyDictionary<string, string> FilterBypassAllowlist { get; init; }
}
```

```csharp fragment
namespace Trax.Effect.Data.Testing;

public static class TraxInvariants
{
    public static Task<IReadOnlyList<TraxInvariantViolation>> FindViolationsAsync(string connectionString, CancellationToken cancellationToken = default);
    public static Task<IReadOnlyList<TraxInvariantViolation>> FindViolationsAsync(string connectionString, IEnumerable<InvokingState> invokingStates, CancellationToken cancellationToken = default);
    public static Task<IReadOnlyList<TraxInvariantViolation>> FindViolationsAsync(DbConnection connection, IEnumerable<InvokingState> invokingStates, CancellationToken cancellationToken = default);
    public static Task<IReadOnlyList<TraxInvariantViolation>> FindViolationsAsync(string connectionString, IEnumerable<InvokingState> invokingStates, IEnumerable<TimedState> timedStates, TimeSpan? maxDraftLifetime = default, int? maxDraftsPerUser = default, IEnumerable<KeptState>? keptStates = null, CancellationToken cancellationToken = default);
    public static Task<IReadOnlyList<TraxInvariantViolation>> FindViolationsAsync(DbConnection connection, IEnumerable<InvokingState> invokingStates, IEnumerable<TimedState> timedStates, TimeSpan? maxDraftLifetime = default, int? maxDraftsPerUser = default, IEnumerable<KeptState>? keptStates = null, CancellationToken cancellationToken = default);
    public static Task AssertConsistentAsync(string connectionString, CancellationToken cancellationToken = default);
    public static Task AssertConsistentAsync(string connectionString, IEnumerable<InvokingState> invokingStates, CancellationToken cancellationToken = default);
    public static string Describe(IReadOnlyCollection<TraxInvariantViolation> violations);
    public static bool IsExempt(Type fixture, string? methodName);
}

public sealed record TraxInvariantViolation(string Invariant, string Table, string Id, string Detail);
public sealed record InvokingState(string Machine, string State);
public sealed record TimedState(string Machine, string State);
public sealed record KeptState(string Machine, string State);

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class LeavesStuckRunsAttribute : Attribute
{
    public LeavesStuckRunsAttribute(string reason);
    public string Reason { get; }
}
```

## Members

### DataLayerGuards

| Checker | Checks | Reads |
|---------|--------|-------|
| `DomainContextsDeriveBase` | Every `*DbContext` class under the source scan roots derives `baseTypeName` (`DomainDataContext<TSelf>` by default). Each class is judged by its own base list, parsed with Roslyn. | Source |
| `CompanionInterfaces` | Every context deriving the base has a companion `I{Name}` interface in the same directory | Source |
| `OneSchemaPerContext` | Each context owns a distinct, non-null default schema; the model is built offline against PostgreSQL | Model |
| `NoPendingModelChanges` | Each migration-based context's EF model matches its latest migration snapshot, so `MigrateAsync` does not trip `PendingModelChangesWarning` at startup. Built offline. | Model |
| `OwnerScopeCompleteness` | Every entity holding per-user data has a row filter that reads the principal accessor, and a `[TraxQueryModel]` among them carries a bare `[TraxAuthorize]` unless it is in `Gated` | Model |
| `OwnerScopeFilterBypasses` | No `IgnoreQueryFilters()` call, and no named-filter disable naming an owner-scope filter, on a per-user set or on a set the scan cannot name, outside `FilterBypassAllowlist` | Source |

The source checkers report a scan root that does not exist as an offender. How the census decides what is per-user, and what neither owner-scope check can see, is in [Architecture Guards](/docs/reference/architecture-guards#the-owner-scope-census).

### DomainDataLayerGuardFixture

| Test | Runs | Passes when not configured |
|------|------|----------------------------|
| `Domain_contexts_derive_the_shared_base` | `DomainContextsDeriveBase(Options, BaseTypeName)` | No: fails on an offender, then on having inspected no context |
| `Domain_contexts_have_companion_interfaces` | `CompanionInterfaces(Options, BaseTypeName)` | No: fails on an offender, then on having inspected no context |
| `Each_domain_context_owns_a_distinct_schema` | `OneSchemaPerContext(DomainContexts)` | Yes, with `DomainContexts` empty |
| `Each_migration_context_matches_its_snapshot` | `NoPendingModelChanges(MigrationContexts)` | Yes, with `MigrationContexts` empty |
| `Every_per_user_entity_is_scoped_to_its_owner` | `OwnerScopeCompleteness` over each of `OwnerScopedModels` | Yes, with `OwnerScopedModels` empty. Set, without `OwnerScope`, it throws `InvalidOperationException`. |
| `Query_code_does_not_switch_an_owner_scope_filter_off` | `OwnerScopeFilterBypasses(Options, OwnerScopedModels, OwnerScope)` | Yes, with `OwnerScopedModels` empty or `ScanForOwnerScopeFilterBypasses` false |

| Property | Default | Description |
|----------|---------|-------------|
| `Options` | (abstract) | Scan roots and allowlists |
| `DomainContexts` | empty | The domain context types |
| `MigrationContexts` | empty | The contexts that use EF migrations; not those bootstrapped with `EnsureSchemaCreatedAsync` |
| `OwnerScopedModels` | empty | One EF model per context holding per-user data, built from a context instance |
| `OwnerScope` | `null` | The census options; required when `OwnerScopedModels` is set |
| `ScanForOwnerScopeFilterBypasses` | `true` | Whether the bypass scan runs |
| `BaseTypeName` | `"DomainDataContext"` | The base the source guards look for |

### OwnerScopeCensusOptions

| Property | Default | Description |
|----------|---------|-------------|
| `OwnerType` | (required) | The entity rows belong to, such as a user profile. A foreign key to it marks the dependent as per-user. |
| `PrincipalAccessorType` | (required) | The type a row filter reads the current principal through. A filter referencing it is an owner-scope filter. |
| `OwnerIdProperties` | empty | Scalar properties holding an owner id without a modelled foreign key, matched by name |
| `NavigationScoped` | empty | Entities that reach their owner only through a navigation, each with the reason |
| `Exemptions` | empty | Per-user entities left out, each with a reason. A blank reason, or an entity the census would not flag, is reported. |
| `Gated` | empty | Per-user query models deliberately gated by a role or policy, each with the reason. The gate is added to the filter, never used in its place. |
| `FilterBypassAllowlist` | empty | Repo-relative source files allowed to switch an owner-scope filter off, each with the reason |

### TraxInvariants

Checks a Trax Postgres or SQLite database once every host a test started has stopped.

| Member | Description |
|--------|-------------|
| `FindViolationsAsync(connectionString)` | Every violation in a Postgres database, ordered by check, then by id |
| `FindViolationsAsync(connectionString, invokingStates)` | The same, and checks that each instance of a machine named in `invokingStates` holds an invoke token exactly when it is in one of those states |
| `FindViolationsAsync(connection, invokingStates)` | The same checks over an open connection, Postgres or SQLite; the caller keeps owning it. Any other connection throws `NotSupportedException`. |
| `FindViolationsAsync(..., timedStates, maxDraftLifetime, maxDraftsPerUser, keptStates)` | Also the revision, owner and row-id checks, the timer checks for the machines whose timed states are passed, the lifetime check with `maxDraftLifetime`, and the per-user cap with `maxDraftsPerUser` |
| `AssertConsistentAsync(connectionString)`, `AssertConsistentAsync(connectionString, invokingStates)` | Throws `InvalidOperationException` listing every violation |
| `Describe(violations)` | That failure message: one line per violation, and how to mark a test that leaves the state on purpose |
| `IsExempt(fixture, methodName)` | True when the test method, its fixture or a base class of the fixture carries `[LeavesStuckRuns]` |

| Constant | Value | Fails on |
|----------|-------|----------|
| `RunInProgress` | `run-in-progress` | A `trax.metadata` row still `in_progress` |
| `EffectClaimInFlight` | `effect-claim-in-flight` | A `trax.effect_claim` row with no receipt |
| `DispatchedWithoutRun` | `dispatched-without-run` | A `trax.work_queue` row `dispatched` whose run does not exist |
| `CheckpointOfCompletedRun` | `checkpoint-of-completed-run` | A `trax.checkpoint` row of a run that completed |
| `ResumeFromMissingRun` | `resume-from-missing-run` | A `trax.work_queue` or `trax.metadata` row whose `resume_from` names no run |
| `InvokeTokenWithoutRun` | `invoke-token-without-run` | A `trax.snapshot_draft` row whose invoke token names no run the instance queued |
| `InvokingStateWithoutToken` | `invoking-state-without-token` | A `trax.snapshot_draft` row in an invoking state with no invoke token, unless `invoke_stranded_state` is its state |
| `InvokeTokenOutsideInvokingState` | `invoke-token-outside-invoking-state` | A `trax.snapshot_draft` row holding an invoke token in a state that invokes nothing |
| `RevisionBelowOne` | `revision-below-one` | A `trax.snapshot_draft` row whose revision is below 1 |
| `UserDraftWithoutOwner` | `user-draft-without-owner` | A user's draft whose `user_key` is empty or whitespace |
| `DuplicateRowId` | `duplicate-row-id` | Two `trax.snapshot_draft` rows sharing one row id |
| `DueAtOutsideTimedState` | `due-at-outside-timed-state` | A due time in a state with no timer |
| `TimedStateWithoutDueAt` | `timed-state-without-due-at` | A state with a timer and no due time |
| `DueAtOfARemovedTimer` | `due-at-of-a-removed-timer` | A due time on a machine whose timed states declare none |
| `DraftOlderThanMaxLifetime` | `draft-older-than-max-lifetime` | A user's draft older than `maxDraftLifetime`, unless it is in one of `keptStates` |
| `DraftsAboveCap` | `drafts-above-cap` | A user holding more drafts than `maxDraftsPerUser`, reported once per user |

`TraxInvariantViolation(Invariant, Table, Id, Detail)` is one violation; its `ToString()` is `[invariant] table id: detail`. On SQLite a violation names its table without the `trax.` schema. `InvokingState`, `TimedState` and `KeptState` name a machine and a state as `trax.snapshot_draft` stores them. `[LeavesStuckRuns("why")]` marks a test method or fixture that leaves one of these states on purpose; `Reason` is the why.

## Examples

```csharp compile
using Trax.Effect.Data.Testing;

var violations = await TraxInvariants.FindViolationsAsync(
    connectionString,
    [new InvokingState("Checkout", "AwaitingPayment")]
);

if (violations.Count > 0)
    throw new InvalidOperationException(TraxInvariants.Describe(violations));
```

The fixture, subclassed in an NUnit test project:

```csharp fragment
[TestFixture]
public sealed class DataLayerGuards : DomainDataLayerGuardFixture
{
    protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["libs", "apps"] };
    protected override IReadOnlyList<Type> DomainContexts => [typeof(CatalogDbContext), typeof(LendingDbContext)];
    protected override IReadOnlyList<Type> MigrationContexts => [typeof(CatalogDbContext)];
}
```

## See also

- [Architecture Guards](/docs/reference/architecture-guards), the owner-scope census and the patterns the guards enforce
- [Trax.Core.Testing](/docs/sdk-reference/testing/core-testing)
- [Domain data contexts](/docs/effect/effect-providers/domain-data-contexts)
