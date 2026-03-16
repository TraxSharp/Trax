using Microsoft.CodeAnalysis;

namespace Trax.Core.Analyzers.Diagnostics;

internal static class DiagnosticDescriptors
{
    /// <summary>
    /// Junction input type not found in Memory.
    /// </summary>
    public static readonly DiagnosticDescriptor JunctionInputNotInMemory = new(
        id: "CHAIN001",
        title: "Junction input type not available in train memory",
        messageFormat: "Junction '{0}' requires input type '{1}' which has not been produced by a previous junction. Available: [{2}].",
        category: "Trax.Core.Train",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    /// <summary>
    /// Resolve return type not found in Memory.
    /// Error because the analyzer now tracks ShortCircuit junctions alongside Chain junctions.
    /// </summary>
    public static readonly DiagnosticDescriptor ResolveTypeNotInMemory = new(
        id: "CHAIN002",
        title: "Train return type not available in memory",
        messageFormat: "Train return type '{0}' has not been produced by any junction. Available: [{1}].",
        category: "Trax.Core.Train",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
}
