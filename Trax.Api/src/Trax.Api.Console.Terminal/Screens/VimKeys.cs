using Terminal.Gui.Input;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>The keys every tab takes: h and l for the tab before and after, 1 to 9 for a tab by number, q to quit.</summary>
internal static class VimKeys
{
    public const string Help = "j/k move  g/G top/end  ^d/^u half page  h/l tab  q quit";

    public static bool Window(ConsoleContext context, Key key)
    {
        switch (Ui.Typed(key))
        {
            case "h":
                context.Tab(-1, null);
                return true;
            case "l":
                context.Tab(1, null);
                return true;
            case "q":
                context.Quit();
                return true;
            case { } typed when typed[0] is >= '1' and <= '9':
                context.Tab(null, typed[0] - '0');
                return true;
            default:
                return false;
        }
    }
}
