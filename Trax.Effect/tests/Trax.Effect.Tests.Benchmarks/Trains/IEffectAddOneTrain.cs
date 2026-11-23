using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Benchmarks.Junctions;
using Trax.Effect.Tests.Benchmarks.Models;

namespace Trax.Effect.Tests.Benchmarks.Trains;

// --- Interfaces ---

public interface IEffectAddOneTrain : IServiceTrain<int, int>;
