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

/// <summary>What every screen reaches through: the terminal, the client, and what the window does for them.</summary>
internal sealed class ConsoleContext(
    IApplication app,
    TraxClient client,
    Uri endpoint,
    IPrompts? prompts = null
)
{
    public IApplication App { get; } = app;

    public TraxClient Client { get; } = client;

    /// <summary>The host's GraphQL endpoint, for the title.</summary>
    public Uri Endpoint { get; } = endpoint;

    public IPrompts Prompts { get; } = prompts ?? new DialogPrompts(app);

    /// <summary>Puts one line in the status line at the bottom of the window.</summary>
    public Action<string> Status { get; set; } = _ => { };

    /// <summary>Opens a run's trace.</summary>
    public Action<long> OpenTrace { get; set; } = _ => { };

    /// <summary>Opens the source of a type a row names (a train, a junction) in the operator's editor.</summary>
    public Action<string> OpenCode { get; set; } = _ => { };

    /// <summary>Moves to the tab so many places on (h, l), or to the tab of a number (1 to 9).</summary>
    public Action<int?, int?> Tab { get; set; } = (_, _) => { };

    /// <summary>Whether machines are drawn top to bottom rather than left to right (r turns them).</summary>
    public bool VerticalGraphs { get; set; }

    /// <summary>Quits the console (q).</summary>
    public Action Quit { get; set; } = () => { };

    private readonly CancellationTokenSource _closed = new();

    /// <summary>
    /// Cancelled when the window this context serves closes, at a quit or when the editor takes the terminal, so a
    /// request still out and work that outlives one (following a bulk operation) stop with it. A screen passes it to
    /// <c>Ui.Start</c> as well, so the cancellation is not reported; a window it opens links its own lifetime to
    /// it.
    /// </summary>
    public CancellationToken Closed => _closed.Token;

    /// <summary>Stops the work that follows <see cref="Closed"/>.</summary>
    public void Close() => _closed.Cancel();
}
