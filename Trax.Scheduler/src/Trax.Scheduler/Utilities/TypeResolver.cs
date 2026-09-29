namespace Trax.Scheduler.Utilities;

/// <summary>
/// Resolves types by fully-qualified name from loaded assemblies.
/// </summary>
/// <remarks>
/// Trax itself no longer calls this. It loads whatever type the name identifies, so it is not a
/// way to turn a stored or received name back into a train input: the dispatcher, the local
/// worker and the runners match those names against the registered trains' input types instead
/// (see scheduler/0006). Kept for compatibility.
/// </remarks>
internal static class TypeResolver
{
    /// <summary>
    /// Resolves a <see cref="Type"/> by its fully-qualified name, searching all loaded assemblies.
    /// </summary>
    /// <param name="typeName">The fully-qualified type name (e.g., "MyApp.Trains.MyInput")</param>
    /// <returns>The resolved <see cref="Type"/></returns>
    /// <exception cref="TypeLoadException">Thrown when the type cannot be found in any loaded assembly</exception>
    public static Type ResolveType(string typeName)
    {
        var type = Type.GetType(typeName);
        if (type is not null)
            return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName);
            if (type is not null)
                return type;
        }

        var namespacePrefix = GetNamespacePrefix(typeName);
        var relevantAssemblies = AppDomain
            .CurrentDomain.GetAssemblies()
            .Where(a =>
                !a.IsDynamic
                && a.GetName().Name is { } name
                && (
                    namespacePrefix.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(namespacePrefix, StringComparison.OrdinalIgnoreCase)
                )
            )
            .Select(a => a.GetName().Name)
            .OrderBy(n => n)
            .ToList();

        var hint =
            relevantAssemblies.Count > 0
                ? $" Assemblies with a matching namespace prefix: [{string.Join(", ", relevantAssemblies)}]"
                : " No loaded assemblies match the type's namespace prefix. Ensure the assembly containing this type is referenced and loaded.";

        throw new TypeLoadException($"Unable to resolve type '{typeName}'.{hint}");
    }

    private static string GetNamespacePrefix(string typeName)
    {
        var lastDot = typeName.LastIndexOf('.');
        return lastDot > 0 ? typeName[..lastDot] : typeName;
    }
}
