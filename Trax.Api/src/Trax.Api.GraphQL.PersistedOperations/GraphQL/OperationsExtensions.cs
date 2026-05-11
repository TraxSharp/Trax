using HotChocolate.Types;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL;

/// <summary>
/// Grafts <c>persistedOperations</c> onto <c>operations</c> on the query side
/// of the schema. Mirrors how <c>operations.deadLetters</c> and
/// <c>operations.manifestGroups</c> are wired by the base
/// <c>Trax.Api.GraphQL</c> package.
/// </summary>
[ExtendObjectType(typeof(OperationsQueries))]
public sealed class OperationsQueriesPersistedOperationsExtension
{
    /// <summary>
    /// Nested namespace exposing persisted-operation queries (paged list,
    /// single lookup, audit history).
    /// </summary>
    public PersistedOperationQueries PersistedOperations() => new();
}

/// <summary>
/// Grafts <c>persistedOperations</c> onto <c>operations</c> on the mutation
/// side of the schema.
/// </summary>
[ExtendObjectType(typeof(OperationsMutations))]
public sealed class OperationsMutationsPersistedOperationsExtension
{
    /// <summary>
    /// Nested namespace exposing persisted-operation mutations (upload,
    /// deactivate, restore).
    /// </summary>
    public PersistedOperationMutations PersistedOperations() => new();
}
