using System.Reflection;
using System.Reflection.Metadata;

namespace Trax.Effect.Utils;

/// <summary>
/// Finds a type by a stored name among the assemblies already loaded, and never loads one. The
/// name is parsed with <see cref="TypeName"/>, and each part of it (the type, its generic
/// arguments, an array's element) is looked up in <see cref="AppDomain.GetAssemblies"/>; an
/// assembly a name points at is matched by its simple name against those, so a name naming an
/// assembly that is not loaded resolves to nothing rather than to a load from disk.
/// </summary>
internal static class LoadedTypeResolver
{
    /// <summary>
    /// Resolves <paramref name="typeName"/> among the loaded assemblies.
    /// </summary>
    /// <param name="typeName">A FullName or an assembly-qualified name.</param>
    /// <param name="allowAssemblyQualified">
    /// When false, a name that names an assembly at its top level is refused; the assembly-qualified
    /// generic arguments a FullName carries are still resolved.
    /// </param>
    /// <returns>The type, or null when the name does not parse or a part of it is not loaded.</returns>
    public static Type? Resolve(string typeName, bool allowAssemblyQualified)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        if (!TypeName.TryParse(typeName.AsSpan(), out var parsed))
            return null;

        if (!allowAssemblyQualified && parsed.AssemblyName is not null)
            return null;

        return Resolve(parsed);
    }

    private static Type? Resolve(TypeName name)
    {
        if (name.IsArray)
        {
            var element = Resolve(name.GetElementType());
            if (element is null)
                return null;
            return name.IsSZArray
                ? element.MakeArrayType()
                : element.MakeArrayType(name.GetArrayRank());
        }

        if (name.IsPointer || name.IsByRef)
            return null;

        if (name.IsConstructedGenericType)
        {
            var definition = Resolve(name.GetGenericTypeDefinition());
            if (definition is null)
                return null;

            var arguments = new List<Type>();
            foreach (var argumentName in name.GetGenericArguments())
            {
                var argument = Resolve(argumentName);
                if (argument is null)
                    return null;
                arguments.Add(argument);
            }

            try
            {
                return definition.MakeGenericType([.. arguments]);
            }
            catch (ArgumentException)
            {
                // The arguments break the definition's constraints, or their count is wrong.
                return null;
            }
        }

        return FindLoaded(name.FullName, name.AssemblyName?.Name);
    }

    /// <summary>
    /// Looks a plain or nested type name up in the loaded assemblies, the one the name points at
    /// first when it points at one, then any of them, which keeps a type that moved assembly
    /// between runtime versions resolvable without loading anything.
    /// </summary>
    private static Type? FindLoaded(string fullName, string? assemblyName)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        if (assemblyName is not null)
            foreach (var assembly in assemblies)
                if (string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                {
                    var type = GetType(assembly, fullName);
                    if (type is not null)
                        return type;
                }

        foreach (var assembly in assemblies)
        {
            var type = GetType(assembly, fullName);
            if (type is not null)
                return type;
        }

        return null;
    }

    private static Type? GetType(Assembly assembly, string fullName)
    {
        try
        {
            return assembly.GetType(fullName, throwOnError: false);
        }
        catch (ArgumentException)
        {
            // GetType refuses an empty or malformed name with ArgumentException rather than null.
            return null;
        }
    }
}
