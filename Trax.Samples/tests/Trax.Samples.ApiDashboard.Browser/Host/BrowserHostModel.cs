using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

// What the browser tests' host runs, so every page they open has something to draw: two trains
// whose canonical names are dotted .NET names, scheduled in two manifest groups with a dependency
// between them (the groups page's dependency graph), and a system-owned machine with an instance
// (the state-machine pages).
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
        m.In(GateState.Closed).On(GateTrigger.Open).To(GateState.Open);
        m.In(GateState.Open).On(GateTrigger.Close).To(GateState.Closed);
        m.SystemOwned();
    }
}

/// <summary>No user owns anything here; only the system's instance exists.</summary>
public sealed class NoSnapshotPrincipal : ISnapshotPrincipal
{
    public string? CurrentUserKey => null;
}
