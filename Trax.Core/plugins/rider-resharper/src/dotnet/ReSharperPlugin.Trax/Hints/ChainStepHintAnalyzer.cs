using System.Collections.Generic;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Psi.Util;
using ReSharperPlugin.Trax.Core.Analysis;

namespace ReSharperPlugin.Trax.Core.Hints
{
    [ElementProblemAnalyzer(
        typeof(IInvocationExpression),
        HighlightingTypes = new[] { typeof(ChainStepInlayHint) }
    )]
    public class ChainStepHintAnalyzer : ElementProblemAnalyzer<IInvocationExpression>
    {
        private static readonly HashSet<string> ChainMethodNames = new HashSet<string>
        {
            "Chain",
            "ShortCircuit",
            "IChain",
        };

        protected override void Run(
            IInvocationExpression element,
            ElementProblemAnalyzerData data,
            IHighlightingConsumer consumer
        )
        {
            // 1. Check if this is a member-access call (receiver.Method<T>())
            var referenceExpression = element.InvokedExpression as IReferenceExpression;
            if (referenceExpression == null)
                return;

            // 2. Quick check: is the method name one we care about?
            var methodName = referenceExpression.NameIdentifier?.Name;
            if (methodName == null || !ChainMethodNames.Contains(methodName))
                return;

            // 3. Resolve the method being called
            var resolveResult = referenceExpression.Reference.Resolve();
            var method = resolveResult.DeclaredElement as IMethod;
            if (method == null)
                return;

            // 4. Get the type argument list from the reference expression
            var typeArgumentList = referenceExpression.TypeArgumentList;
            if (typeArgumentList == null || typeArgumentList.TypeArguments.Count == 0)
                return;

            // 5. Get the step type from the first type argument
            var stepType = typeArgumentList.TypeArguments[0];
            var stepTypeElement = stepType.GetTypeElement<ITypeElement>();
            if (stepTypeElement == null)
                return;

            // 6. Resolve IStep<TIn, TOut> from the step type
            var resolved = PsiStepTypeResolver.Resolve(stepTypeElement);
            if (resolved == null)
                return;

            // 7. Format the hint text using short presentation
            var tInName = resolved.TIn.GetPresentableName(element.Language);
            var tOutName = resolved.TOut.GetPresentableName(element.Language);
            var hintText = tInName + " \u2192 " + tOutName;

            // 8. Full type names for tooltip
            var tInLong = resolved.TIn.GetLongPresentableName(element.Language);
            var tOutLong = resolved.TOut.GetLongPresentableName(element.Language);
            var tooltip = stepTypeElement.ShortName + ": " + tInLong + " \u2192 " + tOutLong;

            // 9. Position the hint after the closing parenthesis of the invocation
            var range = element.GetDocumentRange();
            var offset = range.EndOffset;

            // 10. Emit the highlighting
            consumer.AddHighlighting(new ChainStepInlayHint(hintText, element, offset, tooltip));
        }
    }
}
