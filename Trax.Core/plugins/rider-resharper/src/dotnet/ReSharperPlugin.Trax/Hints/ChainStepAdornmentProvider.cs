using JetBrains.Application.Parts;
using JetBrains.ProjectModel;
using JetBrains.TextControl.DocumentMarkup;
using JetBrains.TextControl.DocumentMarkup.Adornments;

namespace ReSharperPlugin.Trax.Core.Hints
{
    [SolutionComponent(Instantiation.ContainerAsyncAnyThreadUnsafe)]
    public class ChainStepAdornmentProvider : IHighlighterAdornmentProvider
    {
        public bool IsValid(IHighlighter highlighter)
        {
            return highlighter.UserData is ChainStepInlayHintBase;
        }

        public IAdornmentDataModel CreateDataModel(IHighlighter highlighter)
        {
            return highlighter.UserData is ChainStepInlayHint hint
                ? new ChainStepAdornmentDataModel(hint.HintText)
                : null;
        }
    }
}
