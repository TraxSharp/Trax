using System.Text.Json.Nodes;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

// What the browser tests' host runs. Two trains are scheduled at startup in two dependent manifest groups (the
// groups page's dependency graph) and a system-owned machine has an instance (the state-machine pages); the rest
// is what the seed builders (Fixtures/Seed.cs) drive into the states a console page shows: a train that fails
// while its key is down, one that holds while its subject is held, one whose queue entry stays staged, one with
// a Gate and a checkpoint, one that never runs, and two machines, one invoking a train and one with a timer.
namespace Trax.Samples.ApiDashboard.Browser.Host;

public record RatesInput : IManifestProperties
{
    public string Currency { get; init; } = "USD";
}

/// <summary>Scheduled in the <c>ingest</c> group.</summary>
public interface IRefreshRatesTrain : IServiceTrain<RatesInput, Unit>;

public class RefreshRatesTrain : ServiceTrain<RatesInput, Unit>, IRefreshRatesTrain
{
    protected override async Task<Either<Exception, Unit>> Junctions() => Resolve();
}

/// <summary>Runs after <see cref="IRefreshRatesTrain"/>, in the <c>publish</c> group.</summary>
public interface IRepriceCatalogTrain : IServiceTrain<RatesInput, Unit>;

public class RepriceCatalogTrain : ServiceTrain<RatesInput, Unit>, IRepriceCatalogTrain
{
    protected override async Task<Either<Exception, Unit>> Junctions() => Resolve();
}

/// <summary>
/// Registered and never run, scheduled or invoked by anything: the train page's empty state, which still draws
/// its chain.
/// </summary>
public interface IAuditLedgerTrain : IServiceTrain<KeyedInput, Unit>;

public class AuditLedgerTrain : ServiceTrain<KeyedInput, Unit>, IAuditLedgerTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<ReadLedger>().Chain<SealLedger>().Resolve();
}

public class ReadLedger : EffectJunction<KeyedInput, KeyedInput>
{
    public override Task<KeyedInput> Run(KeyedInput input) => Task.FromResult(input);
}

public class SealLedger : EffectJunction<KeyedInput, Unit>
{
    public override Task<Unit> Run(KeyedInput input) => Task.FromResult(Unit.Default);
}

/// <summary>What the host's switchable trains are about: one key per seeded thing.</summary>
public record KeyedInput : IManifestProperties
{
    public string Key { get; init; } = "";
}

/// <summary>Fails while its key is down (<see cref="Outages"/>), and succeeds once it is back.</summary>
public interface IImportFeedTrain : IServiceTrain<KeyedInput, Unit>;

public class ImportFeedTrain : ServiceTrain<KeyedInput, Unit>, IImportFeedTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() => Chain<CallSupplier>().Resolve();
}

public class CallSupplier(Outages outages) : EffectJunction<KeyedInput, Unit>
{
    public override Task<Unit> Run(KeyedInput input) =>
        outages.IsDown(input.Key)
            ? throw new TimeoutException($"The supplier for {input.Key} did not answer.")
            : Task.FromResult(Unit.Default);
}

/// <summary>
/// Holds its run open until the test releases its key (<see cref="Holds"/>). Queued work about one subject runs
/// one at a time, so a second entry for the same key is blocked behind the first.
/// </summary>
public interface IReconcileAccountTrain : IServiceTrain<KeyedInput, Unit>;

public class ReconcileAccountTrain : ServiceTrain<KeyedInput, Unit>, IReconcileAccountTrain
{
    protected override string? QueueSubjectKey(Metadata metadata) => $"account:{TrainInput.Key}";

    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<WaitForLedger>().Resolve();
}

public class WaitForLedger(Holds holds) : EffectJunction<KeyedInput, Unit>
{
    public override async Task<Unit> Run(KeyedInput input)
    {
        await holds.WaitAsync(input.Key, CancellationToken);
        return Unit.Default;
    }
}

/// <summary>
/// Its queue entry is promoted only once its <c>OnQueue</c> hook returns, and the hook waits until the test
/// releases its key: until then the entry is staged.
/// </summary>
public interface ISyncMailboxTrain : IServiceTrain<KeyedInput, Unit>;

public class SyncMailboxTrain(Holds holds) : ServiceTrain<KeyedInput, Unit>, ISyncMailboxTrain
{
    protected override bool DeferQueuePromotion => true;

    protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
        holds.WaitAsync(TrainInput.Key, ct);

    protected override async Task<Either<Exception, Unit>> Junctions() => Resolve();
}

/// <summary>Whether to pay a claim: the train's Gate.</summary>
[Asks(
    "Should this claim be paid without a person reviewing it?",
    Yes = "Pay it: the amount is small.",
    No = "Reject it: the claim does not qualify."
)]
public sealed class PayWithoutReview;

public sealed record Claim(string Key, decimal Amount);

/// <summary>What happened to a claim, which the checkpoint stores.</summary>
public sealed record ClaimOutcome(string Key, string Status);

/// <summary>
/// Loads a claim, asks a decider whether to pay it (a Gate), stores the outcome at a checkpoint, then notifies
/// the claimant, which fails while the claim's key is down: a failed run with a checkpoint.
/// </summary>
public interface ISettleClaimTrain : IServiceTrain<KeyedInput, Unit>;

public class SettleClaimTrain : ServiceTrain<KeyedInput, Unit>, ISettleClaimTrain
{
    // Carried by the chain rather than registered, so no other train sees a decider.
    private static readonly IDecider Decider = new ScriptedDecider().YesNo<PayWithoutReview>(0.9);

    protected override Task<Either<Exception, Unit>> Junctions() =>
        AddServices(Decider)
            .Chain<LoadClaim>()
            .Gate<Claim, PayWithoutReview>(answer =>
                answer
                    .Yes(then => then.Chain<PayClaim>(), atLeast: 0.8)
                    .No(then => then.Chain<RejectClaim>(), below: 0.3)
                    .Unsure(then => then.Chain<RejectClaim>())
            )
            .Checkpoint<ClaimOutcome>()
            .Chain<NotifyClaimant>()
            .Resolve();
}

public class LoadClaim : EffectJunction<KeyedInput, Claim>
{
    public override Task<Claim> Run(KeyedInput input) => Task.FromResult(new Claim(input.Key, 40));
}

public class PayClaim : EffectJunction<Claim, ClaimOutcome>
{
    public override Task<ClaimOutcome> Run(Claim claim) =>
        Task.FromResult(new ClaimOutcome(claim.Key, "Paid"));
}

public class RejectClaim : EffectJunction<Claim, ClaimOutcome>
{
    public override Task<ClaimOutcome> Run(Claim claim) =>
        Task.FromResult(new ClaimOutcome(claim.Key, "Rejected"));
}

public class NotifyClaimant(Outages outages) : EffectJunction<ClaimOutcome, Unit>
{
    public override Task<Unit> Run(ClaimOutcome outcome) =>
        outages.IsDown(outcome.Key)
            ? throw new TimeoutException($"The mail service did not answer for {outcome.Key}.")
            : Task.FromResult(Unit.Default);
}

/// <summary>What <see cref="RelayMachine"/>'s run is handed: the key it holds on (<see cref="Holds"/>).</summary>
public record RelayInput
{
    public string Key { get; init; } = "";
}

/// <summary>The run <see cref="RelayMachine"/> invokes; it holds while its key is held.</summary>
public interface IRelayParcelTrain : IServiceTrain<RelayInput, Unit>;

public class RelayParcelTrain : ServiceTrain<RelayInput, Unit>, IRelayParcelTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<HandOverParcel>().Resolve();
}

public class HandOverParcel(Holds holds) : EffectJunction<RelayInput, Unit>
{
    public override async Task<Unit> Run(RelayInput input)
    {
        await holds.WaitAsync(input.Key, CancellationToken);
        return Unit.Default;
    }
}

public enum GateState
{
    Closed,
    Open,
}

public enum GateTrigger
{
    Open,
    Close,
}

/// <summary>A gate the system owns; the host starts one instance of it.</summary>
public sealed class GateMachine : Machine<GateState, GateTrigger>
{
    public const string Id = "browser-gate";

    protected override void Configure(IMachineBuilder<GateState, GateTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(GateState.Closed, () => new JsonObject());
        m.In(GateState.Closed).Context().On(GateTrigger.Open).To(GateState.Open);
        m.In(GateState.Open).Context().On(GateTrigger.Close).To(GateState.Closed);
        m.SystemOwned();
    }
}

public enum RelayState
{
    Waiting,
    Relaying,
    Relayed,
    Lost,
}

public enum RelayTrigger
{
    Send,
}

public sealed record RelayContext
{
    public string? Key { get; init; }
}

/// <summary>
/// Started with a key in its context; <c>Send</c> enters <c>Relaying</c>, which invokes
/// <see cref="IRelayParcelTrain"/>: a run started by a machine.
/// </summary>
public sealed class RelayMachine : Machine<RelayState, RelayTrigger>
{
    public const string Id = "browser-relay";

    protected override void Configure(IMachineBuilder<RelayState, RelayTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(RelayState.Waiting, () => new JsonObject());
        m.In(RelayState.Waiting)
            .Context<RelayContext>()
            .On(RelayTrigger.Send)
            .To(RelayState.Relaying);
        m.In(RelayState.Relaying)
            .Context<RelayContext>()
            .Invokes<IRelayParcelTrain, RelayInput, Unit>(c => new RelayInput
            {
                Key = c["key"]?.GetValue<string>() ?? "",
            })
            .OnDone(RelayState.Relayed)
            .OnFailed(RelayState.Lost)
            .OnCancelled(RelayState.Lost);
        m.In(RelayState.Relayed).Context<RelayContext>();
        m.In(RelayState.Lost).Context<RelayContext>();
        m.SystemOwned();
    }
}

public enum ReminderState
{
    Pending,
    Reminded,
}

public enum ReminderTrigger
{
    Dismiss,
}

/// <summary>A reminder that fires <see cref="Wait"/> after it starts: the timer an overdue instance has.</summary>
public sealed class ReminderMachine : Machine<ReminderState, ReminderTrigger>
{
    public const string Id = "browser-reminder";

    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(1);

    protected override void Configure(IMachineBuilder<ReminderState, ReminderTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(ReminderState.Pending, () => new JsonObject());
        m.In(ReminderState.Pending)
            .Context()
            .After(Wait)
            .GoTo(ReminderState.Reminded)
            .On(ReminderTrigger.Dismiss)
            .To(ReminderState.Reminded);
        m.In(ReminderState.Reminded).Context();
        m.SystemOwned();
    }
}

/// <summary>No user owns anything here; only the system's instances exist.</summary>
public sealed class NoSnapshotPrincipal : ISnapshotPrincipal
{
    public string? CurrentUserKey => null;
}
