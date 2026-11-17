using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Trax.Core.Analyzers.Analysis;

/// <summary>
/// Represents a single call in the fluent train chain.
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
