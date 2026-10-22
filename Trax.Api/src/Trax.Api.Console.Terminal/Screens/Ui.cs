using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>
/// What every screen needs from the terminal: running a request off the UI thread and showing its answer on it, and
/// the console's dialogs.
/// </summary>
internal static class Ui
{
    /// <summary>
    /// Runs <paramref name="work"/> off the UI thread and hands its result to <paramref name="done"/> on it. An
    /// exception the work throws (a filter the host cannot take, a cancelled request) goes to
    /// <paramref name="failed"/>, also on the UI thread.
    /// </summary>
    public static void Start<T>(
        IApplication app,
        Func<Task<T>> work,
        Action<T> done,
        Action<string> failed
    ) =>
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await work();
                app.Invoke(() => done(result));
            }
            catch (Exception ex)
                when (ex
                        is InvalidOperationException
                            or OperationCanceledException
                            or HttpRequestException
                )
            {
                app.Invoke(() => failed(ex.Message));
            }
        });

    /// <summary>The key's character when it is a plain or shifted letter, digit or sign; null for anything else.</summary>
    public static string? Typed(Key key) =>
        key.IsCtrl || key.IsAlt || key.AsGrapheme is not { Length: 1 } g || char.IsControl(g[0])
            ? null
            : g;

    /// <summary>Asks a question with <paramref name="buttons"/>; the index of the one chosen, or null for Esc.</summary>
    public static int? Ask(
        IApplication app,
        string title,
        string message,
        params string[] buttons
    ) => MessageBox.Query(app, title, message, buttons);

    /// <summary>Says something the operator should read before going on.</summary>
    public static void Tell(IApplication app, string title, string message) =>
        MessageBox.Query(app, title, message, "OK");

    /// <summary>
    /// A form of labelled text fields. Each field is (label, initial value); the values entered, in order, or null
    /// when it was cancelled.
    /// </summary>
    public static IReadOnlyList<string>? Form(
        IApplication app,
        string title,
        IReadOnlyList<(string Label, string Value)> fields,
        string accept = "OK",
        string? clear = null
    )
    {
        var dialog = new Dialog
        {
            Title = title,
            Width = Dim.Percent(70),
            Height = Dim.Auto(),
        };
        var width = fields.Count == 0 ? 0 : fields.Max(f => f.Label.Length) + 2;
        var inputs = new List<TextField>();
        View? above = null;
        foreach (var (label, value) in fields)
        {
            var name = new Label
            {
                Text = label,
                X = 1,
                Y = above is null ? 1 : Pos.Bottom(above),
            };
            var input = new TextField
            {
                Value = value,
                X = width + 1,
                Y = Pos.Top(name),
                Width = Dim.Fill(1),
            };
            dialog.Add(name, input);
            inputs.Add(input);
            above = name;
        }
        int? chosen = null;
        void Button(string text, int index)
        {
            var button = new Button { Text = text };
            button.Accepting += (_, e) =>
            {
                chosen = index;
                dialog.RequestStop();
                e.Handled = true;
            };
            dialog.AddButton(button);
        }
        Button(accept, 0);
        if (clear is not null)
            Button(clear, 1);
        Button("Cancel", 2);
        app.Run(dialog);
        var values = inputs.Select(i => i.Value ?? "").ToList();
        dialog.Dispose();
        return chosen switch
        {
            0 => values,
            1 => fields.Select(_ => "").ToList(),
            _ => null,
        };
    }

    /// <summary>
    /// A read-only, scrolling list of text lines. A line with breaks in it is several, so a stack trace reads as
    /// one.
    /// </summary>
    public static ListView Lines(IEnumerable<string> lines)
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<string>(
            lines.SelectMany(l => l.Replace("\r\n", "\n").Split('\n'))
        );
        return new ListView { Source = new ListWrapper<string>(items) };
    }

    /// <summary>Shows lines of text in a scrolling, read-only window until Esc.</summary>
    public static void Show(IApplication app, string title, IEnumerable<string> lines)
    {
        var dialog = new Dialog
        {
            Title = title,
            Width = Dim.Percent(90),
            Height = Dim.Percent(85),
        };
        var text = Lines(lines);
        text.Width = Dim.Fill();
        text.Height = Dim.Fill(1);
        dialog.Add(text);
        var close = new Button { Text = "Close" };
        close.Accepting += (_, e) =>
        {
            dialog.RequestStop();
            e.Handled = true;
        };
        dialog.AddButton(close);
        app.Run(dialog);
        dialog.Dispose();
    }
}
