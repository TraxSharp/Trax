#if !TRAX_ANALYZERS_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Trax.Analyzers.Roslyn;

#if !TRAX_ANALYZERS_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE
[Microsoft.CodeAnalysis.Embedded]
#endif
internal static partial class SuppressorHelpers
{
    public static SyntaxNode? FindNode(this Diagnostic diagnostic, CancellationToken cancellationToken)
    {
        return FindNode(diagnostic.Location, cancellationToken);
    }

    private static SyntaxNode? FindNode(Location? location, CancellationToken cancellationToken)
    {
        if (location is null)
            return null;

        var syntaxTree = location.SourceTree;
        if (syntaxTree is null)
            return null;

        var root = syntaxTree.GetRoot(cancellationToken);
        return root.FindNode(location.SourceSpan);
    }
}
