using System.Collections.Concurrent;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>
/// A scheduled train that cancels the token its JobRunner was started with as its last act, then
/// completes. Stands in for a host shutdown that lands after the scheduled work finished but
/// before the JobRunner has recorded it.
/// </summary>
public class CancelsItsRunnerTrain
    : ServiceTrain<CancelsItsRunnerInput, Unit>,
        ICancelsItsRunnerTrain
{
    /// <summary>The token sources a test registers, keyed by <see cref="CancelsItsRunnerInput.Key"/>.</summary>
    public static ConcurrentDictionary<string, CancellationTokenSource> Runners { get; } = new();

    protected override Task<Either<Exception, Unit>> Junctions() => Chain<CancelRunner>().Resolve();
}

/// <summary>Input for <see cref="CancelsItsRunnerTrain"/>.</summary>
public record CancelsItsRunnerInput : IManifestProperties
{
    public string Key { get; set; } = string.Empty;
}

/// <summary>Interface for <see cref="CancelsItsRunnerTrain"/>.</summary>
public interface ICancelsItsRunnerTrain : IServiceTrain<CancelsItsRunnerInput, Unit> { }

/// <summary>Cancels the registered runner token and returns without observing it.</summary>
internal sealed class CancelRunner : Junction<CancelsItsRunnerInput, Unit>
{
    public override Task<Unit> Run(CancelsItsRunnerInput input)
    {
        if (CancelsItsRunnerTrain.Runners.TryGetValue(input.Key, out var runner))
            runner.Cancel();

        return Task.FromResult(Unit.Default);
    }
}
