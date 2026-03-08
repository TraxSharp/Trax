namespace Trax.Scheduler.Utilities;

/// <summary>
/// Resolves types by fully-qualified name from loaded assemblies.
/// </summary>
/// <remarks>
/// Used by <see cref="Extensions.JobRunnerExtensions"/> and <c>SqsJobRunnerHandler</c>
/// to deserialize train inputs transmitted as JSON with their type name.
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

        throw new TypeLoadException($"Unable to find type: {typeName}");
    }
}
