namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

/// <summary>
/// Fluent configuration surface passed to
/// <c>TraxGraphQLBuilder.UsePersistedOperations(opts =&gt; ...)</c>. The
/// builder is split across partial files, one per feature area, mirroring
/// the convention used by other Trax builders (see
/// <c>TraxMediatorBuilder</c>, <c>TraxGraphQLBuilder</c>).
/// </summary>
public sealed partial class PersistedOperationsBuilder
{
    // Created by UsePersistedOperations and handed to its callback.
    internal PersistedOperationsBuilder() { }

    // ----- enforcement -----
    private bool _requirePersisted = true;
    private bool _logNonPersistedRequests;

    // ----- allowlist -----
    private readonly HashSet<string> _allowedOperationNames = new(StringComparer.Ordinal);
    private readonly List<Func<string, bool>> _allowOperationPredicates = new();
    private bool _allowIntrospection = true;

    // ----- cache -----
    private bool _cacheEnabled;
    private TimeSpan? _cacheTtl;
    private bool _cacheConfigured;
    private TimeSpan _cacheMaxAge = PersistedOperationsOptions.DefaultCacheMaxAge;

    // ----- broadcasting -----
    private string? _rabbitMqConnectionString;
    private bool _singleNode;

    // ----- schema exposure -----
    private bool _exposeOperationsNamespace = true;
}
