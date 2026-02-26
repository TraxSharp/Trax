using Microsoft.CodeAnalysis;

namespace Trax.Core.Analyzers.Analysis;

/// <summary>
/// Extracts (TIn, TOut) type arguments from a step type by scanning its implemented interfaces
/// for IStep&lt;TIn, TOut&gt;. This is the static (Roslyn) equivalent of
/// ReflectionHelpers.ExtractStepTypeArguments&lt;TStep&gt;().
/// </summary>
internal static class StepTypeResolver
{
    private const string IStepTypeName = "IStep";
    private const string IStepNamespace = "Trax.Core.Step";

    /// <summary>
    /// Resolves the (TIn, TOut) type pair from a step type symbol.
    /// Returns null if the type does not implement IStep&lt;TIn, TOut&gt;.
    /// </summary>
    public static (ITypeSymbol TIn, ITypeSymbol TOut)? Resolve(INamedTypeSymbol stepType)
    {
        foreach (var iface in stepType.AllInterfaces)
        {
            if (
                iface.IsGenericType
                && iface.TypeArguments.Length == 2
                && iface.Name == IStepTypeName
                && iface.ContainingNamespace?.ToDisplayString() == IStepNamespace
            )
            {
                return (iface.TypeArguments[0], iface.TypeArguments[1]);
            }
        }

        return null;
    }
}
