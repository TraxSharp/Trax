using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GameServer.Trains.Maintenance.CorruptedDataRepair;

public interface ICorruptedDataRepairTrain : IServiceTrain<CorruptedDataRepairInput, Unit>;
