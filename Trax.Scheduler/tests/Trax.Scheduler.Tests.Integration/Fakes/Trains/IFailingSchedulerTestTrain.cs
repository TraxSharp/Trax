using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>
/// Interface for the failing scheduler test train.
/// </summary>
public interface IFailingSchedulerTestTrain : IServiceTrain<FailingSchedulerTestInput, Unit>;
