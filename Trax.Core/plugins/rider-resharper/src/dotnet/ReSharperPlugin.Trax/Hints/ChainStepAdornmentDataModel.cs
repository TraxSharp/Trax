using System.Collections.Generic;
using JetBrains.Application.UI.Controls.BulbMenu.Items;
using JetBrains.Application.UI.Controls.Utils;
using JetBrains.Application.UI.PopupLayout;
using JetBrains.TextControl.DocumentMarkup.Adornments;
using JetBrains.UI.Icons;
using JetBrains.UI.RichText;
using JetBrains.Util;

namespace ReSharperPlugin.Trax.Core.Hints
{
    public class ChainStepAdornmentDataModel : IAdornmentDataModel
    {
        public ChainStepAdornmentDataModel(string hintText)
        {
            Text = new RichText(hintText);
        }

        public void ExecuteNavigation(PopupWindowContextSource popupWindowContextSource) { }

        public AdornmentData Data =>
            new AdornmentData(
                Text,
                IconId,
                default,
                AdornmentPlacement.DefaultAfterPrevChar,
                InlayHintsMode
            );

        public RichText Text { get; }
        public IPresentableItem ContextMenuTitle => null;
        public IEnumerable<BulbMenuItem> ContextMenuItems => EmptyList<BulbMenuItem>.Instance;
        public TextRange? SelectionRange => null;
        public IconId IconId => null;
        public PushToHintMode InlayHintsMode => PushToHintMode.Default;
    }
}
