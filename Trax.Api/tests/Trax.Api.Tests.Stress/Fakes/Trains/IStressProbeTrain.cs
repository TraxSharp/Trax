using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.Stress.Fakes.Trains;

public interface IStressProbeTrain : IServiceTrain<StressProbeInput, Unit>;
