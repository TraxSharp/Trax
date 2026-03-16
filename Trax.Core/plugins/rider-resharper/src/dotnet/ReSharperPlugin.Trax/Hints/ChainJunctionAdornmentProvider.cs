using JetBrains.Application.Parts;
using JetBrains.ProjectModel;
using JetBrains.TextControl.DocumentMarkup;
using JetBrains.TextControl.DocumentMarkup.Adornments;

namespace ReSharperPlugin.Trax.Core.Hints
{
    [SolutionComponent(Instantiation.ContainerAsyncAnyThreadUnsafe)]
    public class ChainJunctionAdornmentProvider : IHighlighterAdornmentProvider
    {
        public bool IsValid(IHighlighter highlighter)
        {
            return highlighter.UserData is ChainJunctionInlayHintBase;
        }

        public IAdornmentDataModel CreateDataModel(IHighlighter highlighter)
        {
            return highlighter.UserData is ChainJunctionInlayHint hint
                ? new ChainJunctionAdornmentDataModel(hint.HintText)
                : null;
        }
    }
}
