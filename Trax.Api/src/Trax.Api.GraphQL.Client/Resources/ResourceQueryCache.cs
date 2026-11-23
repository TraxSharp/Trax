using System.Collections.Concurrent;
using System.Reflection;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Loads <c>.graphql</c> embedded resources once per request type and caches the resulting
/// query string statically. The cache is keyed by <see cref="Type"/>, so two unrelated
/// assemblies declaring resources of the same name do not collide.
/// </summary>
internal static class ResourceQueryCache
{
    private static readonly ConcurrentDictionary<Type, string> Cache = new();

    public static async Task<string> GetQueryAsync(Type requestType)
    {
        if (Cache.TryGetValue(requestType, out var cached))
            return cached;
        var loaded = await LoadFromResourceAsync(requestType).ConfigureAwait(false);
        return Cache.GetOrAdd(requestType, loaded);
    }

    private static async Task<string> LoadFromResourceAsync(Type type)
    {
        var attribute =
            type.GetCustomAttribute<GraphQLQueryResourceAttribute>()
            ?? throw new InvalidOperationException(
                $"Request type '{type.FullName}' inherits from GraphQLResourceRequest<T> but "
                    + $"is not decorated with [GraphQLQueryResource(\"...\")]. Add the attribute "
                    + $"pointing to the .graphql resource file."
            );

        var assembly = type.Assembly;
        var candidates = BuildCandidateResourceNames(type, attribute.ResourceName);

        var manifest = assembly.GetManifestResourceNames();

        foreach (var candidate in candidates)
        {
            if (Array.IndexOf(manifest, candidate) >= 0)
                return await ReadResourceAsync(assembly, candidate).ConfigureAwait(false);
        }

        // Last resort, for a request whose namespace does not match the folder its file is in:
        // a resource whose name ends with the given one at a '.' boundary, compared ignoring case
        // (build tools differ on casing between Linux and Windows). A name only one resource ends
        // with is that resource; a name several end with is refused, because picking one by
        // manifest order could send a different document, a mutation included.
        var normalized = Normalize(attribute.ResourceName);
        var matches = manifest
            .Where(name => EndsAtBoundary(name, normalized))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (matches.Count == 1)
            return await ReadResourceAsync(assembly, matches[0]).ConfigureAwait(false);

        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"[GraphQLQueryResource(\"{attribute.ResourceName}\")] on '{type.FullName}' matches "
                    + $"{matches.Count} embedded resources in {assembly.GetName().Name}: "
                    + string.Join(", ", matches)
                    + ". Qualify the name with its folder (for example \"Queries/Player.graphql\") "
                    + "or move the request into the namespace its file is embedded under."
            );

        throw new InvalidOperationException(
            $"Embedded resource for '{type.FullName}' not found. Tried: "
                + string.Join(", ", candidates)
                + $". Available resources in {assembly.GetName().Name}: "
                + (manifest.Length == 0 ? "(none)" : string.Join(", ", manifest))
                + ". Did you add <EmbeddedResource Include=\"**/*.graphql\" /> to the consuming csproj?"
        );
    }

    private static IEnumerable<string> BuildCandidateResourceNames(Type type, string resourceName)
    {
        // Default convention: {Namespace}.{ResourceName} (with subfolders as dots).
        var ns = type.Namespace;
        var normalized = Normalize(resourceName);
        if (!string.IsNullOrEmpty(ns))
            yield return $"{ns}.{normalized}";

        // Some build setups prefix the assembly name instead. Try that too.
        var assemblyName = type.Assembly.GetName().Name;
        if (!string.IsNullOrEmpty(assemblyName))
            yield return $"{assemblyName}.{normalized}";

        // If the resource name already looks fully qualified, accept it as-is.
        yield return normalized;
    }

    private static string Normalize(string resourceName) =>
        resourceName.Replace('/', '.').Replace('\\', '.');

    private static bool EndsAtBoundary(string manifestName, string suffix) =>
        manifestName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
        && (
            manifestName.Length == suffix.Length
            || manifestName[manifestName.Length - suffix.Length - 1] == '.'
        );

    private static async Task<string> ReadResourceAsync(Assembly assembly, string resourceName)
    {
        var stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' was listed in the manifest but stream was null."
            );
        await using var streamDisposal = stream.ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
