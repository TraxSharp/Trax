using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Trax.Core.Analyzers.Analysis;

/// <summary>
/// Represents a single call in the fluent workflow chain.
/// </summary>
internal readonly struct ChainCall
{
    public string MethodName { get; }
    public InvocationExpressionSyntax Invocation { get; }
    public IMethodSymbol Method { get; }

    public ChainCall(string methodName, InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        MethodName = methodName;
        Invocation = invocation;
        Method = method;
    }
}

/// <summary>
/// Parses a fluent workflow chain by unwinding the nested InvocationExpression syntax tree
/// into an ordered list of calls: [Activate, Chain&lt;A&gt;, Chain&lt;B&gt;, ..., Resolve].
/// </summary>
internal static class ChainCallParser
{
    /// <summary>
    /// Starting from a Resolve() invocation, walks inward through the syntax tree
    /// to collect all chained calls in execution order.
    /// Returns null if the chain structure is not recognized.
    /// </summary>
    public static List<ChainCall>? Parse(
        InvocationExpressionSyntax resolveInvocation,
        SemanticModel semanticModel
    )
    {
        var calls = new List<ChainCall>();
        var current = resolveInvocation;

        while (current != null)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(current);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                return null;

            var methodName = methodSymbol.Name;
            calls.Add(new ChainCall(methodName, current, methodSymbol));

            // Walk inward: the receiver of a fluent call is the Expression of the MemberAccess
            if (
                current.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Expression is InvocationExpressionSyntax innerInvocation
            )
            {
                current = innerInvocation;
            }
            else
            {
                // We've hit the innermost call (Activate), which is invoked directly
                // e.g., Activate(input) — not a member access on another invocation
                break;
            }
        }

        // Reverse so we get execution order: Activate first, Resolve last
        calls.Reverse();
        return calls;
    }
}
