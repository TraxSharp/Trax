using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Sandbox.Image;

/// <summary>The same work as <see cref="HostileTrain"/>, undeclared.</summary>
public sealed class UnsandboxedTrain : ServiceTrain<HostileInput, HostileOutput>, IUnsandboxedTrain
{
    /// <inheritdoc />
    protected override Task<Either<Exception, HostileOutput>> Junctions() =>
        Chain<HostileJunction>().Resolve();
}
