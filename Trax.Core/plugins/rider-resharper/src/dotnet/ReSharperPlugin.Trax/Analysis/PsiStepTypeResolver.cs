using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Util;

namespace ReSharperPlugin.Trax.Core.Analysis
{
    /// <summary>
    /// Resolves IStep&lt;TIn, TOut&gt; type arguments from a step class using ReSharper PSI.
    /// Port of Trax.Core.Analyzers.Analysis.StepTypeResolver (Roslyn) to PSI.
    /// </summary>
    internal static class PsiStepTypeResolver
    {
        private const string IStepShortName = "IStep";
        private const string IStepNamespace = "Trax.Core.Step";

        /// <summary>
        /// Given a step type element, finds the IStep&lt;TIn, TOut&gt; interface implementation
        /// and extracts the two substituted type arguments.
        /// Returns null if the type does not implement IStep&lt;TIn, TOut&gt;.
        /// </summary>
        public static StepTypes Resolve(ITypeElement stepTypeElement)
        {
            foreach (var superType in stepTypeElement.GetAllSuperTypes())
            {
                var typeElement = superType.GetTypeElement();
                if (typeElement == null)
                    continue;

                if (typeElement.ShortName != IStepShortName)
                    continue;

                var ns = typeElement.GetContainingNamespace();
                if (ns == null || ns.QualifiedName != IStepNamespace)
                    continue;

                if (typeElement.TypeParameters.Count != 2)
                    continue;

                var substitution = superType.GetSubstitution();
                var tIn = substitution[typeElement.TypeParameters[0]];
                var tOut = substitution[typeElement.TypeParameters[1]];

                return new StepTypes(tIn, tOut);
            }

            return null;
        }
    }

    internal sealed class StepTypes
    {
        public IType TIn { get; }
        public IType TOut { get; }

        public StepTypes(IType tIn, IType tOut)
        {
            TIn = tIn;
            TOut = tOut;
        }
    }
}
