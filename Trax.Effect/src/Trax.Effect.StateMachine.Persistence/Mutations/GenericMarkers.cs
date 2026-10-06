using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.StateMachine.Persistence.Mutations;

// Marker interfaces give each generic train a clean GraphQL response type name AND the canonical
// interface FullName the rest of Trax keys on (metadata.Name, work_queue.train_name, discovery). Each
// extends IServiceTrain<,> with the train's own input/output so AddScopedTraxRoute<IX, X> registers the
// interface as the ServiceType. Four total, shared by every machine (not per-machine).
/// <summary>
/// The service interface of a snapshot mutation train. Its FullName is the train's canonical name (the
/// one recorded in <c>metadata.name</c>) and the DI service type <c>AddStateMachines</c> registers. The four
/// marker interfaces are shared by every machine; resolve them through the mediator rather than
/// implementing them.
/// </summary>
public interface ISaveSnapshot : IServiceTrain<SaveSnapshotInput, SaveSnapshotOutput> { }

/// <inheritdoc cref="ISaveSnapshot" />
public interface IAdvanceSnapshot : IServiceTrain<AdvanceSnapshotInput, AdvanceSnapshotOutput> { }

/// <inheritdoc cref="ISaveSnapshot" />
public interface ILoadSnapshot : IServiceTrain<LoadSnapshotInput, LoadSnapshotOutput> { }

/// <inheritdoc cref="ISaveSnapshot" />
public interface ISendSnapshot : IServiceTrain<SendSnapshotInput, SendSnapshotOutput> { }
