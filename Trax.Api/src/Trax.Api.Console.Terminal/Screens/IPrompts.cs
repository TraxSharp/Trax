using Terminal.Gui.App;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>The questions a screen asks the operator, apart from the screen, so a test can answer them.</summary>
internal interface IPrompts
{
    /// <summary>
    /// A question with <paramref name="choices"/>, the first the default, and a Cancel after them; the index of the
    /// choice taken, or null when cancelled or dismissed.
    /// </summary>
    int? Ask(string title, string message, params string[] choices);

    /// <summary>Something the operator should read before going on.</summary>
    void Tell(string title, string message);

    /// <summary>Labelled text fields; the values entered, in order, or null when cancelled.</summary>
    IReadOnlyList<string>? Form(
        string title,
        IReadOnlyList<(string Label, string Value)> fields,
        string accept = "OK",
        string? clear = null
    );

    /// <summary>Lines of text to read, until closed.</summary>
    void Show(string title, IEnumerable<string> lines);
}
