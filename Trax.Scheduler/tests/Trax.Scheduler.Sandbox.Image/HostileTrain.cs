using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Image;

/// <summary>Runs <see cref="HostileJunction"/>, with limits small enough that the escape tests hit them quickly.</summary>
[TraxSandbox("escape", cpuMillicores: 500, memoryMiB: 192, diskMiB: 8, timeoutSeconds: 30)]
public sealed class HostileTrain : ServiceTrain<HostileInput, HostileOutput>, IHostileTrain
{
    /// <inheritdoc />
    protected override Task<Either<Exception, HostileOutput>> Junctions() =>
        Chain<HostileJunction>().Resolve();
}
