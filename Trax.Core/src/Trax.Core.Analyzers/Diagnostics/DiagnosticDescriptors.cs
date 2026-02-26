using Microsoft.CodeAnalysis;

namespace Trax.Core.Analyzers.Diagnostics;

internal static class DiagnosticDescriptors
{
    /// <summary>
    /// Step input type not found in Memory.
    /// </summary>
    public static readonly DiagnosticDescriptor StepInputNotInMemory =
        new(
            id: "CHAIN001",
            title: "Step input type not available in workflow memory",
            messageFormat: "Step '{0}' requires input type '{1}' which has not been produced by a previous step. Available: [{2}].",
            category: "Trax.Core.Workflow",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

    /// <summary>
    /// Resolve return type not found in Memory.
    /// Error because the analyzer now tracks ShortCircuit steps alongside Chain steps.
    /// </summary>
    public static readonly DiagnosticDescriptor ResolveTypeNotInMemory =
        new(
            id: "CHAIN002",
            title: "Workflow return type not available in memory",
            messageFormat: "Workflow return type '{0}' has not been produced by any step. Available: [{1}].",
            category: "Trax.Core.Workflow",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );
}
