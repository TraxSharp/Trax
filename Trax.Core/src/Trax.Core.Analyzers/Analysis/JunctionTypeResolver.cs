using Microsoft.CodeAnalysis;

namespace Trax.Core.Analyzers.Analysis;

/// <summary>
/// Extracts (TIn, TOut) type arguments from a junction type by scanning its implemented interfaces
/// for IJunction&lt;TIn, TOut&gt;. This is the static (Roslyn) equivalent of
/// ReflectionHelpers.ExtractJunctionTypeArguments&lt;TJunction&gt;().
/// </summary>
internal static class JunctionTypeResolver
{
    private const string IJunctionTypeName = "IJunction";
    private const string IJunctionNamespace = "Trax.Core.Junction";

    /// <summary>
    /// Resolves the (TIn, TOut) type pair from a junction type symbol.
    /// Returns null if the type does not implement IJunction&lt;TIn, TOut&gt;.
    /// </summary>
    public static (ITypeSymbol TIn, ITypeSymbol TOut)? Resolve(INamedTypeSymbol junctionType)
    {
        foreach (var iface in junctionType.AllInterfaces)
        {
            if (
                iface.IsGenericType
                && iface.TypeArguments.Length == 2
                && iface.Name == IJunctionTypeName
                && iface.ContainingNamespace?.ToDisplayString() == IJunctionNamespace
            )
            {
                return (iface.TypeArguments[0], iface.TypeArguments[1]);
            }
        }

        return null;
    }
}
