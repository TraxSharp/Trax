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
    public static ConcurrentDictionary<string, CancellationTokenSource> Runners { get; } =
        new(StringComparer.Ordinal);

    protected override Task<Either<Exception, Unit>> Junctions() => Chain<CancelRunner>().Resolve();
}
