using System.Collections.Concurrent;
using HotChocolate.Execution.Caching;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;

namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>A cached value, the generation it was read in, and when it was read.</summary>
internal readonly record struct Stamped<TValue>(TValue Value, long Generation, long ReadAt);
