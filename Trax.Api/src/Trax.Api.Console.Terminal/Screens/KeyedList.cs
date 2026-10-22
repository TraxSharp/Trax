using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>
/// A list whose keys go to the screen before the list's own type-to-search, which would otherwise take every letter.
/// </summary>
internal sealed class KeyedList : ListView
{
    /// <summary>Handles a key first; true when it was the screen's.</summary>
    public Func<Key, bool>? Keys { get; set; }

    protected override bool OnKeyDown(Key key) => Keys?.Invoke(key) == true || base.OnKeyDown(key);
}
