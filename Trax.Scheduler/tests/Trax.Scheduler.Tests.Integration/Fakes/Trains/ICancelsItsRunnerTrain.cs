using System.Collections.Concurrent;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>Interface for <see cref="CancelsItsRunnerTrain"/>.</summary>
public interface ICancelsItsRunnerTrain : IServiceTrain<CancelsItsRunnerInput, Unit>;
