using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.PersistedOperations.Startup;

/// <summary>
/// What one <c>AddPersistedOperationStore</c> call declared: the options it built, in force in a
/// process that does not call <c>UsePersistedOperations</c> (the last call's), and the gate, so a
/// process that does can check at startup that the two agree.
/// </summary>
internal sealed record PersistedOperationStoreDeclaration(
    PersistedOperationsOptions Options,
    IReadOnlyList<TraxAuthorizeAttribute> ManagementGate
);
