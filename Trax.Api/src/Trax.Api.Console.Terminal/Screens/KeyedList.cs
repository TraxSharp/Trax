using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>
/// A list that moves with vim's keys as well as the arrows (j, k, g, G, Ctrl+d, Ctrl+u, and H, L sideways for lines
/// wider than it, such as a machine's graph), and whose screen sees every key first, before the list's own
/// type-to-search, which would otherwise take every letter.
/// </summary>
internal sealed class KeyedList : ListView
{
    /// <summary>Handles a key first; true when it was the screen's.</summary>
    public Func<Key, bool>? Keys { get; set; }

    protected override bool OnKeyDown(Key key) =>
        Keys?.Invoke(key) == true || Vim(key) || base.OnKeyDown(key);

    private bool Vim(Key key)
    {
        switch (Ui.Typed(key))
        {
            case "H":
                ScrollHorizontal(-8);
                return true;
            case "L":
                ScrollHorizontal(8);
                return true;
        }
        var count = Source?.Count ?? 0;
        if (count == 0)
            return false;
        var at = SelectedItem ?? 0;
        var page = Math.Max(1, Viewport.Height / 2);
        int? to =
            key == Key.D.WithCtrl ? at + page
            : key == Key.U.WithCtrl ? at - page
            : Ui.Typed(key) switch
            {
                "j" => at + 1,
                "k" => at - 1,
                "g" => 0,
                "G" => count - 1,
                _ => null,
            };
        if (to is not { } next)
            return false;
        SelectedItem = Math.Clamp(next, 0, count - 1);
        EnsureSelectedItemVisible();
        return true;
    }
}
