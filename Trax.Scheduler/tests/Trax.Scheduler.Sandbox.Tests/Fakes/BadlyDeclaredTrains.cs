using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Sandbox.Image;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.Fakes;

/// <summary>Trains whose [TraxSandbox] declares limits no hub may send.</summary>
public static class BadlyDeclaredTrains
{
    /// <summary>A train declaring a limit no sandbox can run with.</summary>
    public interface IZeroCpuTrain : IServiceTrain<HostileInput, HostileOutput>;

    /// <inheritdoc cref="IZeroCpuTrain" />
    [TraxSandbox("escape", cpuMillicores: 0, memoryMiB: 192, diskMiB: 8, timeoutSeconds: 30)]
    public sealed class ZeroCpuTrain : ServiceTrain<HostileInput, HostileOutput>, IZeroCpuTrain
    {
        /// <inheritdoc />
        protected override Task<Either<Exception, HostileOutput>> Junctions() =>
            Chain<HostileJunction>().Resolve();
    }

    /// <summary>A train declaring a timeout longer than a hub can wait for.</summary>
    public interface IForeverTrain : IServiceTrain<HostileInput, HostileOutput>;

    /// <inheritdoc cref="IForeverTrain" />
    [TraxSandbox(
        "escape",
        cpuMillicores: 500,
        memoryMiB: 192,
        diskMiB: 8,
        timeoutSeconds: int.MaxValue
    )]
    public sealed class ForeverTrain : ServiceTrain<HostileInput, HostileOutput>, IForeverTrain
    {
        /// <inheritdoc />
        protected override Task<Either<Exception, HostileOutput>> Junctions() =>
            Chain<HostileJunction>().Resolve();
    }
}
