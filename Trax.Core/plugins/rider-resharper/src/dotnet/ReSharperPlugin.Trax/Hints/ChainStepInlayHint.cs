using JetBrains.DocumentModel;
using JetBrains.ReSharper.Daemon;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Feature.Services.InlayHints;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.TextControl.DocumentMarkup;

namespace ReSharperPlugin.Trax.Core.Hints
{
    [RegisterHighlighterGroup(
        HighlightAttributeGroupId,
        HighlightAttributeIdBase,
        HighlighterGroupPriority.CODE_SETTINGS
    )]
    [RegisterHighlighter(
        HighlightAttributeId,
        GroupId = HighlightAttributeGroupId,
        ForegroundColor = "#707070",
        BackgroundColor = "#EBEBEB",
        DarkForegroundColor = "#787878",
        DarkBackgroundColor = "#3B3B3C",
        EffectType = EffectType.INTRA_TEXT_ADORNMENT,
        Layer = HighlighterLayer.ADDITIONAL_SYNTAX,
        TransmitUpdates = true
    )]
    [DaemonAdornmentProvider(typeof(ChainStepAdornmentProvider))]
    [DaemonTooltipProvider(typeof(InlayHintTooltipProvider))]
    [StaticSeverityHighlighting(
        Severity.INFO,
        typeof(HighlightingGroupIds.CodeInsights),
        AttributeId = HighlightAttributeId
    )]
    public class ChainStepInlayHint : ChainStepInlayHintBase
    {
        public const string HighlightAttributeId = nameof(ChainStepInlayHint);

        public string HintText { get; }

        public ChainStepInlayHint(
            string hintText,
            ITreeNode node,
            DocumentOffset offset,
            string tooltip
        )
            : base(node, offset, tooltip)
        {
            HintText = hintText;
        }
    }
}
