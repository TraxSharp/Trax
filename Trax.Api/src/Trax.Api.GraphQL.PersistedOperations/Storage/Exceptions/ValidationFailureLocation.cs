using System.Runtime.InteropServices;

namespace Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

/// <summary>1-based location in the source document.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ValidationFailureLocation(int Line, int Column);
