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
    // ----- enforcement -----
    private bool _requirePersisted = true;
    private bool _logNonPersistedRequests;

    // ----- allowlist -----
    private readonly HashSet<string> _allowedOperationNames = new(StringComparer.Ordinal);
    private readonly List<Func<string, bool>> _allowOperationPredicates = new();
    private bool _allowIntrospection = true;

    // ----- cache -----
    private bool _cacheEnabled;
    private TimeSpan _cacheTtl = TimeSpan.FromMinutes(15);
    private bool _cacheConfigured;

    // ----- broadcasting -----
    private string? _rabbitMqConnectionString;

    // ----- database -----
    private string? _databaseConnectionString;
}
