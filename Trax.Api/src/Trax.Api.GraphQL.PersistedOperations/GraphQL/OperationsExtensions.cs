using HotChocolate.Types;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.Validation;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL;

/// <summary>
/// Grafts <c>persistedOperations</c> onto <c>operations</c> on the query side
/// of the schema. Mirrors how <c>operations.deadLetters</c> and
/// <c>operations.manifestGroups</c> are wired by the base
/// <c>Trax.Api.GraphQL</c> package.
/// </summary>
[ExtendObjectType(typeof(OperationsQueries))]
internal sealed class OperationsQueriesPersistedOperationsExtension
{
    /// <summary>
    /// Nested namespace exposing persisted-operation queries (paged list,
    /// single lookup, audit history). It groups fields and reads nothing itself, so it is as open
    /// as the <c>operations</c> field above it: the gate the host declared for the namespace is
    /// on that field, and every field under it is behind it.
    /// </summary>
    [NamespaceField]
    [TraxAllowAnonymous]
    public PersistedOperationQueries PersistedOperations() => new();
}

/// <summary>
/// Grafts <c>persistedOperations</c> onto <c>operations</c> on the mutation
/// side of the schema.
/// </summary>
[ExtendObjectType(typeof(OperationsMutations))]
internal sealed class OperationsMutationsPersistedOperationsExtension
{
    /// <summary>
    /// Nested namespace exposing persisted-operation mutations (upload,
    /// deactivate, restore). Open as far as its parent is, as on the query side.
    /// </summary>
    [NamespaceField]
    [TraxAllowAnonymous]
    public PersistedOperationMutations PersistedOperations() => new();
}
