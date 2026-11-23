using Trax.Effect.Services.ServiceTrain;

namespace Trax.StateMachine.Persistence.Mutations;

/// <inheritdoc cref="ISaveSnapshot" />
public interface IStartSnapshot : IServiceTrain<StartSnapshotInput, StartSnapshotOutput>;
