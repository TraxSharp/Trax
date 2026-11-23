using Terminal.Gui.App;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>The console's dialogs.</summary>
internal sealed class DialogPrompts(IApplication app) : IPrompts
{
    public int? Ask(string title, string message, params string[] choices) =>
        Ui.Ask(app, title, message, choices);

    public void Tell(string title, string message) => Ui.Tell(app, title, message);

    public IReadOnlyList<string>? Form(
        string title,
        IReadOnlyList<(string Label, string Value)> fields,
        string accept = "OK",
        string? clear = null
    ) => Ui.Form(app, title, fields, accept, clear);

    public void Show(string title, IEnumerable<string> lines) => Ui.Show(app, title, lines);
}
