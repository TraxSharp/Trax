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

    public static string GetQuery(Type requestType) =>
        Cache.GetOrAdd(requestType, LoadFromResource);

    private static string LoadFromResource(Type type)
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
                return ReadResource(assembly, candidate);
        }

        // Last resort: case-insensitive endsWith match (defensive against build-tool casing
        // surprises on Linux vs Windows).
        foreach (var manifestName in manifest)
        {
            foreach (var candidate in candidates)
            {
                if (
                    manifestName.EndsWith(candidate, StringComparison.OrdinalIgnoreCase)
                    || manifestName.EndsWith(
                        attribute.ResourceName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    return ReadResource(assembly, manifestName);
            }
        }

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
        var normalized = resourceName.Replace('/', '.').Replace('\\', '.');
        if (!string.IsNullOrEmpty(ns))
            yield return $"{ns}.{normalized}";

        // Some build setups prefix the assembly name instead. Try that too.
        var assemblyName = type.Assembly.GetName().Name;
        if (!string.IsNullOrEmpty(assemblyName))
            yield return $"{assemblyName}.{normalized}";

        // If the resource name already looks fully qualified, accept it as-is.
        yield return normalized;
    }

    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using var stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' was listed in the manifest but stream was null."
            );
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
