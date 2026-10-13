namespace Trax.Effect.Attributes;

/// <summary>
/// Exposes a registered train as a GraphQL mutation by registration rather than by a <see cref="TraxMutationAttribute"/>
/// on its class, so registering the train's route and serving it to clients are two decisions. A subsystem adds one to
/// the service collection, as a singleton instance, only when the host opted in to serving the train; train discovery
/// reads it as it reads the attribute, and adds <see cref="Policy"/> to the train's required policies.
/// </summary>
/// <param name="Implementation">The train class the exposure applies to.</param>
/// <param name="Mutation">The mutation the train is served as, exactly as the attribute would declare it.</param>
/// <param name="Policy">An authorization policy every caller of the mutation must satisfy, or null for none.</param>
internal sealed record TrainMutationExposure(
    Type Implementation,
    TraxMutationAttribute Mutation,
    string? Policy
);
