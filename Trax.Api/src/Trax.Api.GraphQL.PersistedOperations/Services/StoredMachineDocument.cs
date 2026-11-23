using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Trax.Api.GraphQL.TypeModules;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>What the store holds for one machine document id: the version it registered under, and whether it is active.</summary>
internal readonly record struct StoredMachineDocument(int Version, bool IsActive);
