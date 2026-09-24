---
layout: default
title: Trains & Junctions
parent: Core
nav_order: 1
---

# Trains & Junctions

## Junctions

Junctions are the points along a train's route. Each one does one thing:

```csharp
public class ValidateEmailJunction(IUserRepository UserRepository) : Junction<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        var existingUser = await UserRepository.GetByEmailAsync(input.Email);
        if (existingUser != null)
            throw new ValidationException($"Email {input.Email} already exists");

        return Unit.Default;
    }
}

public class CreateUserJunction(IUserRepository UserRepository) : Junction<CreateUserRequest, User>
{
    public override async Task<User> Run(CreateUserRequest input)
    {
        var user = new User
        {
            Email = input.Email,
            FullName = $"{input.FirstName} {input.LastName}",
            CreatedAt = DateTime.UtcNow
        };

        return await UserRepository.CreateAsync(user);
    }
}
```

Junctions use constructor injection for dependencies. When a junction throws, the train switches to the left track and returns the exception.

## CancellationToken in Junctions

Every junction has a `CancellationToken` property that is set automatically by the train before `Run()` is called. Use it to pass cancellation to async operations:

```csharp
public class FetchUserJunction(IHttpClientFactory httpFactory) : Junction<UserId, UserProfile>
{
    public override async Task<UserProfile> Run(UserId input)
    {
        var client = httpFactory.CreateClient();
        var response = await client.GetAsync($"/users/{input.Value}", CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserProfile>(CancellationToken);
    }
}
```

The token comes from the caller: `train.Run(input, cancellationToken)`. If no token is provided, `CancellationToken` defaults to `CancellationToken.None`. Before each junction executes, cancellation is checked. If the token is already cancelled, the junction is skipped and `OperationCanceledException` propagates.

*Full details: [Cancellation Tokens](/docs/cross-cutting/cancellation-tokens)*

## EffectJunction vs Junction

Trax has two junction base classes:

**`Junction<TIn, TOut>`** is the base class. Handles input/output and railway error propagation. No metadata, no lifecycle hooks. Use this for lightweight junctions or when running inside a plain `Train`.

**`EffectJunction<TIn, TOut>`** extends `Junction` with per-junction metadata tracking. When run inside a `ServiceTrain`, it records a `JunctionMetadata` entry with the junction's name, input/output types, start/end times, and railway state. Junction effect providers (like `AddJunctionLogger`) hook into `EffectJunction`'s lifecycle and fire before and after each junction executes.

```csharp
// Base junction, no metadata tracking
public class ValidateEmailJunction(IUserRepository repo) : Junction<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        if (await repo.GetByEmailAsync(input.Email) != null)
            throw new ValidationException("Email already exists");
        return Unit.Default;
    }
}

// Effect junction, tracked by junction effect providers
public class ValidateEmailJunction(IUserRepository repo) : EffectJunction<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        if (await repo.GetByEmailAsync(input.Email) != null)
            throw new ValidationException("Email already exists");
        return Unit.Default;
    }
}
```

The implementation is identical. Just swap the base class. `EffectJunction` only adds metadata when running inside a `ServiceTrain`. If you use `EffectJunction` inside a plain `Train`, it throws at runtime.

Use `EffectJunction` when you want junction-level observability (timing, logging via `AddJunctionLogger`). Use `Junction` when you don't need it.

## Dependency Injection in Junctions

Junctions use standard constructor injection for their dependencies. Do **not** use the `[Inject]` attribute. That's used internally by the `ServiceTrain` base class for its own framework-level services.

```csharp
// Don't use [Inject] in your junctions
public class MyJunction : Junction<Input, Output>
{
    [Inject]
    public IMyService MyService { get; set; }
}

// Use constructor injection
public class MyJunction(IMyService MyService) : Junction<Input, Output>
{
    public override async Task<Output> Run(Input input)
    {
        var result = await MyService.DoSomethingAsync(input);
        return result;
    }
}
```

## Train Structure

As your application grows, you'll want a consistent way to organize trains. Group each train with its input, interface, and junctions in a single folder:

```
Trains/
├── CreateUser/
│   ├── CreateUserRequest.cs        # Input model
│   ├── ICreateUserTrain.cs         # Interface
│   ├── CreateUserTrain.cs          # Implementation
│   └── Junctions/
│       ├── ValidateEmailJunction.cs
│       ├── CreateUserInDatabaseJunction.cs
│       └── SendWelcomeEmailJunction.cs
│
├── ProcessOrder/
│   ├── ProcessOrderRequest.cs
│   ├── IProcessOrderTrain.cs
│   ├── ProcessOrderTrain.cs
│   └── Junctions/
│       ├── ValidateOrderJunction.cs
│       ├── ChargePaymentJunction.cs
│       └── CreateShipmentJunction.cs
```

### The Input Model

Each train gets its own request type. This is required by the `TrainBus` because input types must be unique across your application:

```csharp
namespace YourApp.Trains.CreateUser;

public record CreateUserRequest
{
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}
```

### The Interface

Define an interface for DI resolution and testing:

```csharp
namespace YourApp.Trains.CreateUser;

public interface ICreateUserTrain : IServiceTrain<CreateUserRequest, User>;
```

### The Junctions Folder

Junctions go in a `Junctions/` subfolder. Mark them `internal` since they're implementation details of this train:

```csharp
namespace YourApp.Trains.CreateUser.Junctions;

internal class ValidateEmailJunction(IUserRepository userRepository) : Junction<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        var existing = await userRepository.GetByEmailAsync(input.Email);
        if (existing != null)
            throw new ValidationException($"Email {input.Email} already exists");

        return Unit.Default;
    }
}
```

Using `internal` keeps your public API surface clean. External code interacts with `ICreateUserTrain`, not individual junctions.

### When to Share Junctions

Sometimes multiple trains need the same validation or transformation. Resist the urge to share junctions too early. Duplication is often cheaper than the wrong abstraction.

When you do need to share, create a `Shared/` folder at the `Trains/` level:

```
Trains/
├── Shared/
│   └── Junctions/
│       └── ValidateEmailFormatJunction.cs
├── CreateUser/
│   └── ...
├── UpdateUser/
│   └── ...
```

Shared junctions should be truly generic. If you find yourself adding conditionals to handle different trains, that's a sign the junction should be duplicated and specialized instead.

## Defining a Train

Override `Junctions()` to define the route, the sequence of junctions the train passes through:

```csharp
public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<ValidateEmailJunction>()
            .Chain<CreateUserJunction>().Resolve();
}
```

`Junctions()` returns `Task<Either<Exception, TReturn>>`, and the chain ends in `.Resolve()`, which yields the train's return value from Memory or the exception that stopped the chain. The framework seeds Memory with the input before calling it. Chain methods (`Chain`, `ShortCircuit`, `Extract`, `AddServices`) are available as protected methods on the train itself, and each returns a `MonadTask` so the calls chain fluently.

### There is no escape hatch, on purpose

`Junctions()` is the only way to declare a chain. `RunInternal` is private and `Activate` is
internal, so a train cannot build its chain imperatively. Upgrading a train that used them is
covered in [Removal of RunInternal and Activate](/docs/migration-guides/runinternal-and-activate).

That is what makes a chain readable before it runs. A chain assembled in code has no single
shape, so the host could not check it at startup, and a chain that varies by input would mean the
shape verified is not necessarily the shape that runs. To keep the guarantee, the declaration has
to be the only option.

Two rules follow from it:

**A chain cannot read the value being processed.** `TrainInput` and `TrainOutput` throw while a
chain is being declared. Work that needs the input belongs in a junction, which receives it as
its argument.

**A value a later junction reads comes from something the chain declares.** Usually that is an
earlier junction: its return value lands in Memory keyed by its output type. The chain can also
seed Memory directly with `AddServices(value)` (stored under the interface type it names) or
`Extract<TIn, TOut>(value)` (stores the `TOut` member of the value it is given), and the startup
check records those as seeds. What is gone is `Activate`'s way
of handing extra objects in from outside the declaration.

```csharp
public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<StampCorrelationId>()   // work that used to sit above the chain
            .Chain<ValidateEmailJunction>()
            .Chain<CreateUserJunction>()
            .Resolve();
}
```

`Resolve()` on its own ends a chain that declares no junctions, for a train whose return type is
already in Memory because it is the input type or `Unit`.

### The host checks every chain before it serves traffic

Because a chain is a declaration of types, whether it can run is decidable without running it. At
startup Trax reads every registered train's chain and refuses to start if one of them cannot run.
The check is a hosted service that `AddMediator` registers, so it runs wherever the generic host
starts hosted services; the Lambda runner and a bare `ServiceProvider` never run it. It refuses to
start when:

- the chain could not be read, because it reads the input
- `Junctions()` throws anything else, for example a `NullReferenceException` from reading
  `Metadata`, which is null at startup
- `Junctions()` awaits something before returning, which means it does work instead of declaring
  a chain
- `Junctions()` returns a result directly instead of ending in `Resolve()`, for example
  `Task.FromResult(value)`
- the chain ends in `Resolve(value)`, which states the result instead of naming the junction that
  produces it (a train with no junctions uses `Task.FromResult(Resolve())`)
- a junction needs something in Memory that nothing before it produces
- an `IChain` step names a class rather than an interface, or names a junction interface that
  neither Memory nor the container holds
- `AddServices<T>` names a class rather than an interface
- `Chain<T>` or `ShortCircuit<T>` names a type that is not a junction
- a `ShortCircuit` junction's output cannot be the train's return type (its value is returned as
  the result by a cast, which would fail on every run)
- the chain ends without the train's return type in Memory

Every train is checked before anything is reported, so one start tells you about all of them.

A train the check cannot build is logged as a warning and skipped, not refused: one that cannot be
constructed at startup (its constructor needs a service only a request provides, such as a current
user read from `HttpContext`), or a registered train that does not derive from `Train<,>`. A train
that cannot be built is not evidence of a chain that cannot run, but its chain goes unverified, so
read the warning. A train that *can* be built but whose `Junctions()` throws is a different case,
and is refused as above.

#### What the check counts as available

The replay follows the same rules the runtime uses to store and find values in Memory:

| Value | Available under |
|---|---|
| The train's input | Its declared type and every interface that type implements. A run stores the input under both its declared type and its runtime type (with interfaces), so a junction taking the declared base type works whatever subtype a caller passes |
| An element of a tuple | Its declared type and every interface that type implements. A run stores each element under its declared type as well as its runtime type and interfaces, so a junction taking an element's declared base type works; a null element is skipped, and a junction that needed it fails the ordinary way |
| A junction's output, an `Extract` result, an `AddServices` value | Exactly its declared type, nothing else |
| A `ShortCircuit` junction's output | Not counted. At runtime a short circuit that returns `Right` does store its output and the chain keeps running (later junctions still execute), but one that returns `Left` stores nothing and the chain also keeps running. A later junction or `Resolve()` cannot rely on a value that is only there on one of the two paths, so the check requires something else to produce it |

Lookup is by exact type, then the container, with one exception: a junction whose input is a
tuple has each element assembled from Memory only, never from the container, because that is how a
run builds it. A junction asking for an interface that the previous
junction's declared output only implements is a fault, because the run would not find it either.
`Extract` reads Memory only and never falls back to the container. Container availability is
checked without constructing anything, so a service whose factory only works inside a request does
not crash startup. Reading a chain runs no junction: a monad the train creates while declaring,
including through `NewMonad()`, records its steps instead of executing them.

#### Reading the input inside `Junctions()`

The first of those is the one most likely to bite on upgrade. `Junctions()` declares a chain; it
does not process a value, so there is no input to read yet. `TrainInput` and `TrainOutput` throw
`ChainDeclarationException` while a chain is being declared, and the startup check turns that into
a refused start.

```csharp
protected override Task<Either<Exception, Unit>> Junctions()
{
    // Throws: there is no input at declaration time.
    if (!string.IsNullOrEmpty(TrainInput.ExternalId))
        this.ExternalId = TrainInput.ExternalId;

    return Chain<DoWorkJunction>().Resolve();
}
```

Work that needs the input belongs in a junction, which receives it, or (for something that must
happen as the mutation is accepted) in [`OnQueue`](#onqueue-enqueue-time-hook), which is handed a
`Metadata` carrying the input.

This is a compile-clean change that only surfaces at startup, so a consumer upgrading has no way to
discover it beforehand. If an upgrade is blocked on it, `SkipChainVerification()` turns the check
off while the chains are moved over. It silences every other fault in the list too, so during a
migration it is a stopgap to remove once the chains pass, not a setting to leave on.

The replay knows the train's declared input type, not the subtype a caller passes at runtime, so a
junction asking for an interface that only the runtime subtype implements reads as a fault although
the run would find it. Declaring the input as that subtype fixes both. That blind spot is the one
reason to leave the check off for good; the other reason to turn it off is temporary, while a
codebase whose chains do not pass yet is being migrated:

```csharp
.AddMediator(mediator => mediator.SkipChainVerification())
```

## Train Lifecycle Hooks

`ServiceTrain` provides `protected virtual` methods you can override to react to your train's own lifecycle events, with no global hook registration needed:

```csharp
public class CreateUserTrain(ISlackClient slack)
    : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<ValidateEmailJunction>()
            .Chain<CreateUserJunction>()
            .Resolve();

    protected override async Task OnFailed(
        Metadata metadata, Exception exception, CancellationToken ct)
    {
        await slack.PostAsync($"User creation failed: {exception.Message}", ct);
    }
}
```

Available overrides: `OnStarted`, `OnCompleted`, `OnFailed`, `OnCancelled`. All default to no-op. Exceptions in overrides are caught and logged and never cause the train to fail. Each hook receives the metadata populated with everything known at that point: `OnStarted` sees the typed input (via `TrainInput` or `metadata.GetInput<T>()`); `OnCompleted` additionally sees the output; `OnFailed`/`OnCancelled` see the failure state.

These work alongside [global lifecycle hooks](/docs/sdk-reference/configuration/add-lifecycle-hook). Global hooks fire first, then per-train overrides.

### OnQueue (enqueue-time hook)

`OnQueue` is a fifth override that fires at a different moment from the others: synchronously inside the mediator's QUEUE path (`ITrainExecutionService.QueueAsync`), **before** the work queue row is committed (or, for a train that [defers promotion](#making-the-side-effect-durable), after it is staged unconfirmed and before it is confirmed). The other four fire when the train *runs*; `OnQueue` fires when a QUEUE mutation is *accepted*, before the train is scheduled. It does not fire on the synchronous RUN path, and it does not fire again when the scheduler later runs the train.

Use it to perform a side-effect the instant a mutation is accepted, instead of waiting for the deferred run. A common case is an optimistic write: persist a provisional row so the change is visible immediately, then let the real run reconcile it.

```csharp
public class ProcessMatchResultTrain
    : ServiceTrain<ProcessMatchResultInput, ProcessMatchResultOutput>, IProcessMatchResultTrain
{
    [Inject]
    public IDbContextFactory<GameDbContext>? GameDbFactory { get; set; }

    protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
    {
        var input = metadata.GetInput<ProcessMatchResultInput>();
        if (input is null || GameDbFactory is null)
            return;

        await using var db = await GameDbFactory.CreateDbContextAsync(ct);
        db.Matches.Add(new MatchRecord { MatchId = input.MatchId, Region = input.Region });
        await db.SaveChangesAsync(ct);
    }
}
```

`OnQueue` differs from the other four hooks in three ways:

- **Exceptions propagate.** A throw is not swallowed: it aborts the enqueue and leaves no work queue row behind. Use it only for work that must succeed for the mutation to be accepted.
- **The train is not initialized.** `this.Metadata` and `TrainInput` are unavailable. Read everything from the passed `metadata`: the input via `metadata.GetInput<T>()`, and `metadata.ExternalId` to correlate with the eventual run, which executes under the same ExternalId. `Id`, `ManifestId`, and `ScheduledTime` are unset because no run exists yet.
- **It must be idempotent.** The deferred run re-executes the full `Junctions()` chain, so any effect the chain also performs will happen again. Write `OnQueue` so running it plus the chain is safe.

Property dependencies marked `[Inject]` (like `GameDbFactory` above) are populated before `OnQueue` is called, the same as during a normal run. Trains that do not override `OnQueue` skip resolution entirely, so the enqueue path is unaffected.

#### Making the side-effect durable

The hook and the work queue row are two writes. If the process dies between them, the side-effect can be left with no queued work to consume it: a provisional row nothing will ever reconcile. There are two ways to close that, and which one applies depends on where the hook writes.

**Writing through Trax's own context.** `IEnqueueContextAccessor.Current` exposes the data context the enqueue is about to commit on. Anything tracked on it is committed with the work queue row and rolled back with it:

```csharp
protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
{
    await accessor.Current!.Track(someTraxEntity);   // no SaveChanges: the enqueue commits it
}
```

`Current` is non-null only while `OnQueue` is running for a train that does not defer promotion, and the hook must not call `SaveChanges` or commit on it: the enqueue owns the lifetime. This only covers entities in Trax's own model. The context is entered only for trains that override `OnQueue`.

`Current` flows with the async call, not with the service instance or its scope: every accessor instance on the same async flow sees it, including a singleton train's or one resolved from another scope. See [IEnqueueContextAccessor](/docs/sdk-reference/mediator-api/i-enqueue-context-accessor). Two enqueues running at once on one scope each see their own context, and an enqueue started from inside an `OnQueue` hook gets its own context and transaction for as long as it runs, then hands the outer one back. Nesting an enqueue inside a hook does not corrupt the outer one, but it is not part of it either: the inner enqueue commits on its own context and connection before the outer enqueue's `SaveChanges`, so if the outer hook later throws or the outer commit fails, the inner entry survives the rollback and will run.

The enqueue holds its data context, and with it a pooled database connection with an open transaction, for the whole time the hook runs. A slow hook (one calling a remote API, say) therefore keeps a connection and a transaction open on Trax's database for that long. A deferring train (below) does not: its entry is committed on a context that is released before the hook runs, so for a slow hook that writes nothing through `IEnqueueContextAccessor.Current`, `DeferQueuePromotion` also keeps the connection free.

**Writing through your own `DbContext`.** EF can only share a transaction between contexts that share a connection, so a separately-registered context (the common case, and the one in the example above) commits independently and cannot be rolled back with the entry. For that, defer promotion:

```csharp
protected override bool DeferQueuePromotion => true;
```

The entry is then committed **unconfirmed** and is not dispatchable. The hook runs. A second commit stamps `confirmed_at` and the entry becomes claimable. This does not make the two writes atomic (nothing can, across two databases), but it makes a failure *findable*: a crash leaves an unconfirmed entry instead of an invisible side-effect.

`IEnqueueContextAccessor.Current` is null inside a deferring train's hook. The entry is already committed by then, so there is no enqueue transaction to join, and deferral exists for hooks that write elsewhere.

Once the hook has returned, the mutation counts as accepted and its side-effect may have landed, so the promotion runs even if the caller has cancelled. If the entry was cancelled while the hook ran (by an operator, or by the stale sweep below), there is nothing to promote: `QueueAsync` throws `QueuedWorkCancelledException` (an `InvalidOperationException` carrying the entry's `WorkQueueId`) saying the work will not run and the hook's side-effect may already have been applied, rather than reporting success. If the sweep promoted it instead, because the host opted into promotion, the entry will run and the enqueue succeeds. A hook that *throws* still aborts the enqueue outright: the staged entry is removed, again regardless of cancellation, but only while it is still staged, so a promoted or dispatched entry is never deleted. A failure to remove it does not replace the hook's exception.

An entry a crash left unconfirmed is resolved by the scheduler. On each cycle the [ManifestManager](/docs/scheduler/admin-trains/manifest-manager#resolvestalestagedentriesjunction) finds entries unconfirmed for longer than `StaleStagedEntryTimeout` (10 minutes by default) and **cancels** them. Cancelling is the default because nothing recorded tells a hook that succeeded from one that never ran or one that rejected the mutation, and only the first is a mutation that was accepted. The cancelled entry stays visible, so a side-effect the hook may have left can be found and reconciled. A host whose deferring trains re-check in their chain whatever the hook checked, and whose hooks are idempotent, can call `PromoteStaleStagedEntries()` to have them promoted instead. Keep the timeout well above your slowest hook: an entry resolved while its hook is still running is resolved wrongly. The sweep runs in the ManifestManager, so it does not run while the ManifestManager is disabled (`SchedulerConfiguration.ManifestManagerEnabled = false`, also the dashboard's Server Settings switch); at least one host sharing the database must run the ManifestManager. `Trax.Docs/adr/0018` records why.

**Upgrade every dispatcher first.** A dispatcher from an earlier Trax version claims entries without checking `confirmed_at`, so during a rolling deploy it would dispatch a staged entry before its hook returned. Finish upgrading every host that runs the JobDispatcher before any train overrides `DeferQueuePromotion`.

Deferral is opt-in because it costs an extra round trip and earns nothing when the hook writes nowhere Trax's transaction cannot reach. **Immediate promotion is the default**, and a train that does not override `OnQueue` never stages anything.

### Classifying failures

`OnFailed` hands you an `Exception`. Deciding from it whether to retry, rebuild against current state, or give up usually means matching on a message, and doing that at every call site is how a vendor's wording change breaks three things at once.

Register a classifier instead:

```csharp
public class NetSuiteFailureClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception is NetSuiteRestException { StatusCode: HttpStatusCode.BadRequest } ex
        && ex.ErrorDetails.Any(d => d.Detail.Contains("Record has been changed"))
            ? FailureClass.Conflict
            : null;
}
```

Register it as an ordinary service:

```csharp
services.AddSingleton<IFailureClassifier, NetSuiteFailureClassifier>();
```

Trax resolves a single `IFailureClassifier` from the train's service provider, so only one is used. If several are registered, the container hands back the last registration and the others are never asked.

Trax records the answer on the run as `metadata.FailureClass`, readable in `OnFailed` and persisted, so "how many conflicts this hour" is a query rather than a log trawl. The vocabulary is `Unclassified | Transient | Conflict | Permanent`.

The classifier runs where the failure happened, holding the **original** exception object: junctions enrich an exception and return it rather than wrapping it, so you can type-check and read structured error data instead of parsing text.

Four things worth knowing:

- **Registering one is optional.** With none, every failure records `Unclassified`, which means "decide as you did before this existed". Nothing in Trax acts on a classification yet (retry is still purely count-based), so adding a classifier changes what is recorded, not what happens.
- **Returning null is fine** and means the same as not recognising the failure.
- **Throwing is not fatal.** The classifier's exception is logged and the failure records as `Unclassified`. A classifier must never be able to mask the failure it was asked about.
- **Cancellation is not a failure** and is not classified. That includes an `HttpClient` timeout: it surfaces as `TaskCanceledException`, the run records as `Cancelled`, and the classifier never sees it. A timeout your code turns into its own exception type is a failure like any other and can be classified `Transient`.

A class the failure already carries wins over the local classifier. That is the case for a failure from a remote worker, and for one a calling-side junction preserved when it enriched the exception. A failure rebuilt from a serialized record (a remote failure) is never passed to the local classifier at all: its original type is gone, so a catch-all classifier would stamp a class on something the worker deliberately left alone. The classifier's answer applies to everything else, including a failure raised outside any junction, whose class is attached to the exception so a remote worker reports it too.

Failures the scheduler records itself, such as a dispatch failure or a run failed by the stale-run reaper, record `Unclassified`: no train saw an exception to classify.

**Runs executed remotely are classified too**, but by the worker rather than the caller. The worker holds the real exception, so it classifies there and the answer travels back with the failure. That means the classifier must be registered in the **worker** process: one registered only on the calling side is never asked about a remote run. The calling side records what it was told, over HTTP or Lambda, instead of re-deriving it from a rebuilt exception whose type is gone. A worker that sends no class (an older one, or one whose classifier did not recognise the failure) records `Unclassified` rather than failing, and the calling side does not fill the gap. A class the calling side does not know, sent by a newer worker, also records `Unclassified`, and the worker's error is kept.

For work sent through a job submitter there is nothing to carry: the worker is given the metadata id and writes to that same row, so its classification is already the one you read.

`Trax.Docs/adr/0020` records the reasoning.

### QueueSubjectKey (serializing work that touches the same thing)

Queued entries are claimed by several workers at once, keyed on nothing, so two mutations for the same record can run simultaneously. Against a system that resolves concurrent writes by last-write-wins, that is a lost update.

A train can say what its work touches:

```csharp
protected override string? QueueSubjectKey(Metadata metadata) =>
    $"customer-{metadata.GetInput<PatchCustomerInput>()!.CustomerId}";
```

The key is an opaque string. Trax compares it and nothing else, so its shape is yours to choose. A record identity is the usual pick. It is read at enqueue time from a metadata carrying the input, so it varies per mutation rather than being fixed per train.

**Keys are compared exactly, case-sensitively, across all trains.** They are not namespaced by train: two trains returning `"42"` serialize against each other. Prefix the key with something the train owns (`customer-`, above) unless serializing across trains is what you want.

Returning null, which is the default, means no serialization. Every train that does not override this is unaffected. An empty string is refused, because it is almost always an unset identity and would serialize every train returning it against every other. The key is limited to 512 characters; use a record identity, or a hash of a longer one. Both refusals throw `InvalidOperationException` at enqueue, where the caller sees them.

**Throwing aborts the enqueue.** A key that cannot be computed must not quietly become null: that would drop the guarantee at exactly the moment the caller was relying on it.

Three limits worth knowing:

- **Only queued work is serialized.** Entries created through the mediator's queue path carry a key, including the dashboard's queue dialog and re-queue button. Work queued from a manifest is not about a record and has no subject, and neither does a dormant dependent a parent train activates (`IDormantDependentContext.ActivateAsync`): its input is chosen at runtime by the parent's code, and its entry skips `QueueSubjectKey` and `OnQueue`. A synchronous run (`RunAsync`, `ITrainBus`) never consults the key, so it can overlap a queued run for the same subject.
- **Ordering within a subject is dispatch order**, priority first and then age, so a higher-priority entry for the same subject still goes first. (Dispatch order leads with the manifest group's priority, but an entry queued through the mediator has no manifest, so that does not separate entries for one subject.) An entry whose `scheduledAt` has not arrived is not a candidate at all, so a younger entry that is due runs before an older one scheduled later. That holds with a single dispatcher. With `MaxConcurrentDispatch` above 1, or several hosts dispatching, only mutual exclusion is guaranteed, not order.
- **The guarantee is bounded by the stale-run reapers and the scheduler's startup recovery.** See below.
- **The key and the priority both come from the caller.** `QueueSubjectKey` computes the key from the input the caller supplied, and the queue priority is a caller-supplied argument, so a caller authorized to queue a keyed train chooses which subject its entry serializes against and where it sits in that subject's order. Keys are a single global space rather than one per train, so two trains returning the same string serialize against each other. Authorize the record in `OnQueue`, or scope the key per tenant (`$"{tenantId}:order:{orderId}"`), so a caller cannot name a subject that is not theirs.

**What "in flight" means.** A subject is busy while a dispatched entry for it has a run that has not reached a terminal state, the same definition used elsewhere for an active execution. The dispatcher will not claim an entry whose subject is busy; the entry stays queued and is picked up on a later cycle, so nothing is skipped or lost. Dispatch also offers only the first queued entry per subject in each cycle, so a younger sibling waits behind an older one. The dashboard's work queue detail page shows either case under **Waiting On**, naming the entry whose run holds the subject or the older sibling ahead of it.

That ties the block to run completion, and therefore to whatever writes a terminal state for a run that did not finish on its own. "In flight" ends at any terminal state, including `Failed` set by:

- a stale-run reaper in the ManifestManager: after `StalePendingTimeout` (20 minutes by default) for a run that never started, or `StaleInProgressTimeout` (60 minutes by default) for one that did;
- the scheduler's startup recovery (`RecoverStuckJobsOnStartup`, on by default): when any scheduler host starts, it fails **every** `InProgress` run in the shared database whose start time precedes its own start, whichever host was running it.

A worker killed mid-run leaves its metadata non-terminal, and the subject stays blocked until one of those fails it. But neither can tell a dead worker from a slow one: a run still working when the reaper's timeout passes, or when another host restarts, is failed too, the subject is released, and the next entry for it can run alongside it. **Serialization holds only while none of those intervene**, so keep `StaleInProgressTimeout` above the longest run of any train that sets a subject, and expect a scheduler restart to release every subject whose run was in progress.

Two cases hold a subject longer. A train excluded with `ExcludeFromMaxActiveJobs<T>()` is skipped by both reapers, so if its worker dies its subject stays held until a scheduler host restarts (and a run of it that never left `Pending` is not released by the restart either). And the reapers run in the ManifestManager, so where the ManifestManager is disabled on every host, only a restart releases a subject.

**Upgrade every dispatcher first.** A dispatcher from an earlier Trax version claims without the subject check or lock, so while any old dispatcher is still running, two entries for one subject can run at once. Finish upgrading every host that runs the JobDispatcher before any train overrides `QueueSubjectKey`.

Serialization is enforced in the claim, not just in candidate selection, because two entries for one subject are two different rows: row locking does not make them contend, and while both are still queued neither can see a dispatched sibling to refuse itself. On Postgres the claim takes a transaction-scoped advisory lock on the subject first. That lock is held only for the claim (which commits before the job is submitted), so nothing remote happens while it is held. SQLite has a single writer and needs no lock. [Multi-Server Concurrency](/docs/scheduler/concurrency#subject-serialization-advisory-lock-per-subject) has the details, and `Trax.Docs/adr/0019` records the reasoning.

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Resolve](/docs/sdk-reference/train-methods/resolve) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [IWorkQueuePromotion](/docs/sdk-reference/scheduler-api/i-work-queue-promotion) | [IEnqueueContextAccessor](/docs/sdk-reference/mediator-api/i-enqueue-context-accessor) | [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/train-execution)
