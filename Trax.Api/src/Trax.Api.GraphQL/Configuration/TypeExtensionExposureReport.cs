namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// One field the census rejected, carried from the type interceptor that found it to the
/// startup validator that reports it. The message is built where the violation is found, because
/// not every rejection is one of <see cref="ExposureViolation"/>'s three answers.
/// </summary>
internal sealed record TypeExtensionExposureViolation(string FieldPath, string Message);

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
