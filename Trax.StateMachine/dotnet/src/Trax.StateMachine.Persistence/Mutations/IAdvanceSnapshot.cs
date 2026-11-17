using Trax.Effect.Services.ServiceTrain;

namespace Trax.StateMachine.Persistence.Mutations;

/// <inheritdoc cref="ISaveSnapshot" />
public interface IAdvanceSnapshot : IServiceTrain<AdvanceSnapshotInput, AdvanceSnapshotOutput>;
