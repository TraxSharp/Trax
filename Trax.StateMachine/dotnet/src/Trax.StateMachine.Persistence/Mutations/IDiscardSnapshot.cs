using Trax.Effect.Services.ServiceTrain;

namespace Trax.StateMachine.Persistence.Mutations;

/// <inheritdoc cref="ISaveSnapshot" />
public interface IDiscardSnapshot : IServiceTrain<DiscardSnapshotInput, DiscardSnapshotOutput>;
