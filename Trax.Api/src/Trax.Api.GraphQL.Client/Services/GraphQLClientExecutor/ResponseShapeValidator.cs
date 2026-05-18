using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Compares a single JSON object element against the writable properties of a CLR type and
/// reports drift (extra JSON fields, missing fields). Results are cached per (Type, sorted
/// JSON property set) so the same shape is only walked once even across thousands of calls.
///
/// Validation only runs for objects: scalars, lists, and nulls pass through. For nested
/// objects, callers are expected to surface their own property type at the appropriate
/// nesting level (this is a one-level shape check, not a deep traversal). One level catches
/// the most common drift cause (top-level field added/removed) at low cost.
/// </summary>
internal static class ResponseShapeValidator
{
    private record ShapeCacheKey(Type Type, string JsonShape);

    private static readonly ConcurrentDictionary<ShapeCacheKey, DriftResult> Cache = new();

    private static readonly ConcurrentDictionary<Type, HashSet<string>> PropertyNameCache = new();

    public static void Validate(
        JsonElement element,
        Type targetType,
        ResponseStrictness strictness,
        JsonSerializerOptions options,
        ILogger? logger
    )
    {
        if (strictness == ResponseStrictness.Lenient)
            return;

        if (element.ValueKind != JsonValueKind.Object)
            return;

        var jsonFields = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in element.EnumerateObject())
            jsonFields.Add(prop.Name);

        var key = new ShapeCacheKey(targetType, string.Join("|", jsonFields));
        var drift = Cache.GetOrAdd(key, k => ComputeDrift(k.Type, jsonFields, options));

        if (!drift.HasDrift)
            return;

        switch (strictness)
        {
            case ResponseStrictness.WarnOnDrift:
                logger?.LogWarning(
                    "GraphQL response shape drift for {Type}: extra=[{Extra}] missing=[{Missing}]",
                    targetType.Name,
                    string.Join(",", drift.ExtraJsonFields),
                    string.Join(",", drift.MissingJsonFields)
                );
                break;

            case ResponseStrictness.ThrowOnDrift:
                throw new GraphQLResponseShapeException(
                    targetType,
                    drift.ExtraJsonFields,
                    drift.MissingJsonFields
                );
        }
    }

    private static DriftResult ComputeDrift(
        Type targetType,
        SortedSet<string> jsonFields,
        JsonSerializerOptions options
    )
    {
        var pocoNames = PropertyNameCache.GetOrAdd(targetType, t => BuildPocoNameSet(t, options));

        var extra = jsonFields.Where(j => !pocoNames.Contains(j)).ToArray();
        var missing = pocoNames.Where(p => !jsonFields.Contains(p)).ToArray();

        return new DriftResult(extra, missing);
    }

    /// <summary>
    /// Returns the single canonical JSON name we expect for each property: the explicit
    /// <see cref="JsonPropertyNameAttribute"/> if present, otherwise the policy-applied form
    /// (typically camelCase given <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/>
    /// is true). We use case-insensitive comparison so a server returning <c>id</c> matches a
    /// POCO property named <c>Id</c> without spurious drift reports.
    /// </summary>
    private static HashSet<string> BuildPocoNameSet(Type type, JsonSerializerOptions options)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
                continue;

            var explicitName = prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
            var name =
                explicitName ?? options.PropertyNamingPolicy?.ConvertName(prop.Name) ?? prop.Name;

            set.Add(name);
        }
        return set;
    }

    private sealed record DriftResult(
        IReadOnlyList<string> ExtraJsonFields,
        IReadOnlyList<string> MissingJsonFields
    )
    {
        public bool HasDrift => ExtraJsonFields.Count > 0 || MissingJsonFields.Count > 0;
    }
}
