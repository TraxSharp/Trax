using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Benchmarks.Junctions;
using Trax.Effect.Tests.Benchmarks.Models;

namespace Trax.Effect.Tests.Benchmarks.Trains;

public interface IEffectAddOneX10Train : IServiceTrain<int, int>;
