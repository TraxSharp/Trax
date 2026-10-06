using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.EnergyHub.Trains.Microgrid.OptimizeMicrogrid;

public interface IOptimizeMicrogridTrain : IServiceTrain<OptimizeMicrogridInput, Unit>;
