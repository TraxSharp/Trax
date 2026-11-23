namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// Collects what <see cref="TypeExtensionExposureInterceptor"/> finds while HotChocolate builds
/// the schema, so <see cref="Startup.TypeExtensionExposureValidator"/> can report every offending
/// field at once instead of failing on the first.
/// </summary>
/// <remarks>
/// Deduplicated by schema coordinate: HotChocolate can build the schema more than once in a
/// process (an executor eviction rebuilds it), and the same field found twice is one problem,
/// not two.
/// </remarks>
internal sealed class TypeExtensionExposureReport
{
    private readonly Dictionary<string, TypeExtensionExposureViolation> _violations = new(
        StringComparer.Ordinal
    );
    private readonly Lock _gate = new();

    public void Add(TypeExtensionExposureViolation violation)
    {
        lock (_gate)
            _violations[violation.FieldPath] = violation;
    }

    /// <summary>Ordered by schema coordinate so the failure message is stable between runs.</summary>
    public IReadOnlyList<TypeExtensionExposureViolation> Violations
    {
        get
        {
            lock (_gate)
                return _violations
                    .Values.OrderBy(v => v.FieldPath, StringComparer.Ordinal)
                    .ToList();
        }
    }
}
