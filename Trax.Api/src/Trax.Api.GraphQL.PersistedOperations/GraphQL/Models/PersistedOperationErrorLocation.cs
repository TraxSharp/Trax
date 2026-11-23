using System.ComponentModel;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;

/// <summary>1-based location in the candidate document.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record PersistedOperationErrorLocation(int Line, int Column);
