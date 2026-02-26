using JetBrains.DocumentModel;
using JetBrains.ReSharper.Feature.Services.InlayHints;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.UI.RichText;

namespace ReSharperPlugin.Trax.Core.Hints
{
    public abstract class ChainStepInlayHintBase : IInlayHintWithDescriptionHighlighting
    {
        public const string HighlightAttributeIdBase = nameof(ChainStepInlayHintBase);
        public const string HighlightAttributeGroupId = HighlightAttributeIdBase + "Group";

        private readonly DocumentOffset _offset;
        private readonly ITreeNode _node;

        protected ChainStepInlayHintBase(ITreeNode node, DocumentOffset offset, string tooltip)
        {
            _node = node;
            _offset = offset;
            ToolTip = tooltip;
        }

        public bool IsValid()
        {
            return _node.IsValid();
        }

        public DocumentRange CalculateRange()
        {
            return new DocumentRange(_offset);
        }

        public RichText Description => "Trax.Core: Step type flow hint";
        public string ToolTip { get; }
        public string ErrorStripeToolTip => ToolTip;
    }
}
