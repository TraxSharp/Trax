using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>
/// A test train designed to generate memory allocation patterns for leak testing.
/// This train creates large JsonDocument objects to amplify any memory leaks.
/// </summary>
public class MemoryTestTrain : ServiceTrain<MemoryTestInput, MemoryTestOutput>, IMemoryTestTrain
{
    protected override Task<Either<Exception, MemoryTestOutput>> Junctions() =>
        Chain<AllocateMemoryTestOutput>().Resolve();
}
