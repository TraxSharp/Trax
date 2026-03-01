using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Trax.Core.Analyzers.Analysis;
using Trax.Core.Analyzers.Diagnostics;

namespace Trax.Core.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WorkflowChainAnalyzer : DiagnosticAnalyzer
{
    private const string MonadTypeName = "Monad";
    private const string MonadNamespace = "Trax.Core.Monad";
    private const string UnitMetadataName = "LanguageExt.Unit";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.StepInputNotInMemory,
            DiagnosticDescriptors.ResolveTypeNotInMemory
        );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Only trigger on Resolve() calls to analyze the full chain once
        if (!IsResolveCall(invocation, context.SemanticModel))
            return;

        // Verify the receiver is a Workflow<,> subclass
        var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (methodSymbol?.ContainingType == null)
            return;

        var monadType = FindMonadType(methodSymbol.ContainingType);
        if (monadType == null)
            return;

        // Parse the fluent chain
        var calls = ChainCallParser.Parse(invocation, context.SemanticModel);
        if (calls == null || calls.Count == 0)
            return;

        // Verify chain starts with Activate
        if (calls[0].MethodName != "Activate")
            return;

        // Get monad type parameters: Monad<TInput, TReturn>
        var tInput = monadType.TypeArguments[0];
        var tReturn = monadType.TypeArguments[1];

        // Find the Unit type symbol
        var unitType = context.Compilation.GetTypeByMetadataName(UnitMetadataName);

        // Simulate Memory
        var memory = new MemorySimulator();
        memory.Initialize(tInput, unitType);

        // Process each call in order
        for (var i = 0; i < calls.Count; i++)
        {
            var call = calls[i];

            switch (call.MethodName)
            {
                case "Activate":
                    // Memory is already seeded with TInput and Unit.
                    // Also add types from any additional arguments (params object[] otherInputs).
                    HandleActivateOtherInputs(context, call, memory);
                    break;

                case "Chain":
                case "ShortCircuit":
                    AnalyzeChainCall(context, call, memory);
                    break;

                case "AddServices":
                    HandleAddServices(call, memory);
                    break;

                case "IChain":
                    // Interface-based chaining resolves the step from Memory.
                    // Track the output type if possible.
                    HandleIChain(call, memory);
                    break;

                case "Extract":
                    // Extract<TIn, TOut>() — adds TOut to Memory.
                    HandleExtract(call, memory);
                    break;

                case "Resolve":
                    AnalyzeResolveCall(context, call, tReturn, unitType, memory);
                    break;

                // Unknown methods are silently skipped
            }
        }
    }

    private static void AnalyzeChainCall(
        SyntaxNodeAnalysisContext context,
        ChainCall call,
        MemorySimulator memory
    )
    {
        // Get the TStep type argument from Chain<TStep>()
        var stepType = GetSingleTypeArgument(call.Method);
        if (stepType == null)
            return;

        // Resolve TIn/TOut from the step's IStep<TIn, TOut> interface
        var stepTypes = StepTypeResolver.Resolve(stepType);
        if (stepTypes == null)
            return;

        var (tIn, tOut) = stepTypes.Value;

        // Check that TIn is available in Memory
        if (IsValueTuple(tIn))
        {
            // Tuple input: verify each component type is in Memory
            var tupleType = (INamedTypeSymbol)tIn;
            var missing = memory.GetMissingTupleComponents(tupleType);

            if (missing.Count > 0)
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.StepInputNotInMemory,
                    GetPreciseLocation(call.Invocation),
                    stepType.Name,
                    tIn.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    memory.GetAvailableTypesString()
                );
                context.ReportDiagnostic(diagnostic);
            }
        }
        else if (!memory.Contains(tIn))
        {
            // Non-tuple: direct check. Interface inputs work automatically
            // because AddType now stores all interfaces alongside concrete types.
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.StepInputNotInMemory,
                GetPreciseLocation(call.Invocation),
                stepType.Name,
                tIn.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                memory.GetAvailableTypesString()
            );
            context.ReportDiagnostic(diagnostic);
        }

        // Add TOut to Memory (MemorySimulator skips Unit internally)
        memory.AddType(tOut);
    }

    private static void AnalyzeResolveCall(
        SyntaxNodeAnalysisContext context,
        ChainCall call,
        ITypeSymbol tReturn,
        INamedTypeSymbol? unitType,
        MemorySimulator memory
    )
    {
        // If TReturn is Unit, it's always available — skip check
        if (unitType != null && SymbolEqualityComparer.Default.Equals(tReturn, unitType))
            return;

        // Tuple return types: check each component is in Memory
        if (IsValueTuple(tReturn))
        {
            var tupleType = (INamedTypeSymbol)tReturn;
            var missing = memory.GetMissingTupleComponents(tupleType);

            if (missing.Count > 0)
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.ResolveTypeNotInMemory,
                    GetPreciseLocation(call.Invocation),
                    tReturn.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    memory.GetAvailableTypesString()
                );
                context.ReportDiagnostic(diagnostic);
            }

            return;
        }

        if (!memory.Contains(tReturn))
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.ResolveTypeNotInMemory,
                GetPreciseLocation(call.Invocation),
                tReturn.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                memory.GetAvailableTypesString()
            );
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void HandleActivateOtherInputs(
        SyntaxNodeAnalysisContext context,
        ChainCall call,
        MemorySimulator memory
    )
    {
        var arguments = call.Invocation.ArgumentList.Arguments;

        // Skip the first argument (TInput, already seeded via Initialize)
        for (var i = 1; i < arguments.Count; i++)
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(arguments[i].Expression);
            if (typeInfo.Type != null)
            {
                memory.AddType(typeInfo.Type);
            }
        }
    }

    private static void HandleAddServices(ChainCall call, MemorySimulator memory)
    {
        // AddServices<T1, T2, ...>() — add all type arguments to Memory
        foreach (var typeArg in call.Method.TypeArguments)
        {
            memory.AddType(typeArg);
        }
    }

    private static void HandleIChain(ChainCall call, MemorySimulator memory)
    {
        // IChain<TStep>() — resolves a step from Memory by interface type.
        // Try to get TIn/TOut from the interface to track the output.
        var stepType = GetSingleTypeArgument(call.Method);
        if (stepType == null)
            return;

        var stepTypes = StepTypeResolver.Resolve(stepType);
        if (stepTypes == null)
            return;

        memory.AddType(stepTypes.Value.TOut);
    }

    private static void HandleExtract(ChainCall call, MemorySimulator memory)
    {
        // Extract<TIn, TOut>() — adds TOut to Memory
        if (call.Method.TypeArguments.Length >= 2)
        {
            memory.AddType(call.Method.TypeArguments[1]);
        }
    }

    /// <summary>
    /// Checks if an invocation is a call to a method named "Resolve" on a Workflow type.
    /// </summary>
    private static bool IsResolveCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    )
    {
        // Quick syntax check before doing expensive semantic analysis
        var methodName = GetMethodName(invocation);
        if (methodName != "Resolve")
            return false;

        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return false;

        // Verify receiver is Monad<,>
        return FindMonadType(methodSymbol.ContainingType) != null;
    }

    /// <summary>
    /// Checks the type (and its base types) for Monad&lt;TInput, TReturn&gt;.
    /// Returns the constructed generic type (with concrete type arguments), or null.
    /// </summary>
    private static INamedTypeSymbol? FindMonadType(INamedTypeSymbol type)
    {
        var current = type;
        while (current != null)
        {
            if (
                current.IsGenericType
                && current.TypeArguments.Length == 2
                && current.Name == MonadTypeName
                && current.ContainingNamespace?.ToDisplayString() == MonadNamespace
            )
            {
                return current;
            }

            current = current.BaseType;
        }

        return null;
    }

    /// <summary>
    /// Gets the single type argument from a generic method like Chain&lt;TStep&gt;().
    /// For the overload Chain&lt;TStep&gt;() that we care about, the method itself has
    /// type arguments that get resolved via reflection at runtime. We need to find TStep
    /// from the method's type arguments.
    /// </summary>
    private static INamedTypeSymbol? GetSingleTypeArgument(IMethodSymbol method)
    {
        // Chain<TStep>() at the call site has its type arguments in TypeArguments.
        // But the actual method signature may have more type parameters (TStep, TIn, TOut)
        // that get inferred. We want the first one, which is always TStep.
        if (
            method.TypeArguments.Length >= 1
            && method.TypeArguments[0] is INamedTypeSymbol namedType
        )
            return namedType;

        return null;
    }

    /// <summary>
    /// Returns true if the type is a System.ValueTuple (tuple types).
    /// Phase 1 skips analysis for tuple inputs since the runtime assembles them from components.
    /// </summary>
    private static bool IsValueTuple(ITypeSymbol type)
    {
        return type is INamedTypeSymbol named && named.IsTupleType;
    }

    /// <summary>
    /// Extracts the method name from an invocation expression via syntax only (fast path).
    /// </summary>
    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null,
        };
    }

    /// <summary>
    /// Gets a precise diagnostic location for a chained invocation.
    /// For member access calls like .Chain&lt;StepB&gt;(), returns the span of just
    /// "Chain&lt;StepB&gt;()" rather than the entire chain from Activate().
    /// </summary>
    private static Location GetPreciseLocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var span = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                memberAccess.Name.SpanStart,
                invocation.Span.End
            );
            return Location.Create(invocation.SyntaxTree, span);
        }

        return invocation.GetLocation();
    }
}
