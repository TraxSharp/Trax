using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>
/// A train that intentionally throws exceptions to test error handling memory behavior.
/// </summary>
public interface IFailingTestTrain : IServiceTrain<FailingTestInput, MemoryTestOutput>;
