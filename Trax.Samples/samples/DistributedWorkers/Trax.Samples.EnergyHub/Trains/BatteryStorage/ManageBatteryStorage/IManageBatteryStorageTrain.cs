using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.EnergyHub.Trains.BatteryStorage.ManageBatteryStorage;

public interface IManageBatteryStorageTrain
    : IServiceTrain<ManageBatteryStorageInput, ManageBatteryStorageOutput>;
