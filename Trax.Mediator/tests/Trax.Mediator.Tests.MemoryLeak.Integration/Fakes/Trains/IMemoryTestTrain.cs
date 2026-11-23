using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>
/// Interface for the memory test train.
/// </summary>
public interface IMemoryTestTrain : IServiceTrain<MemoryTestInput, MemoryTestOutput>;
