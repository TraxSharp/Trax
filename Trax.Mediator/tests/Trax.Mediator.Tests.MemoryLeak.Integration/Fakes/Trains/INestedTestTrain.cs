using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>
/// A train that creates nested child trains to test hierarchical memory patterns.
/// </summary>
public interface INestedTestTrain : IServiceTrain<NestedTestInput, NestedTestOutput>;
