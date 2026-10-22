using System.Collections.ObjectModel;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Trax.Api.Console.Terminal.Trace;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>
/// One run as a timeline: its steps and decisions as they happened, then its log, with the steps followed live while
/// the window is open. Esc closes it; o opens the source of the step under the cursor.
/// </summary>
internal sealed class RunTraceWindow : Window
{
    private readonly ConsoleContext _context;
    private readonly long _run;
    private readonly Label _header;
    private readonly KeyedList _lines;
    private readonly ObservableCollection<string> _text = [];
    private readonly List<TraceEntry> _entries = [];
    private IDisposable? _stream;

    public RunTraceWindow(ConsoleContext context, long run)
    {
        _context = context;
        _run = run;
        Title = $"Run {run}";
        _header = new Label { Width = Dim.Fill(), Text = "Reading..." };
        _lines = new KeyedList
        {
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Source = new ListWrapper<string>(_text),
        };
        _lines.Keys = OnKey;
        Add(_header, _lines);
        // Read once the window runs, so every answer lands on the UI thread.
        IsRunningChanged += (_, e) =>
        {
            if (!e.Value || _stream is not null)
                return;
            _lines.SetFocus();
            Load();
            _stream = RunTrace.Follow(
                context.Client,
                run,
                step => context.App.Invoke(() => Upsert(step)),
                () => context.App.Invoke(Load),
                reason =>
                    context.App.Invoke(() => _header.Text = $"{Summary()}  ·  not live: {reason}")
            );
        };
    }

    /// <summary>The type the operator asked to open, read by whoever ran the window once it closed.</summary>
    public string? CodeToOpen { get; private set; }

    /// <summary>The lines shown, in order.</summary>
    public IReadOnlyList<TraceEntry> Entries => _entries;

    private void Load() =>
        Ui.Start(
            _context.App,
            () => RunTrace.LoadAsync(_context.Client, _run, CancellationToken.None),
            answer =>
            {
                if (answer.Data is not { } entries)
                {
                    _header.Text = $"Refused: {answer.Message}";
                    return;
                }
                _entries.Clear();
                _entries.AddRange(entries);
                Render();
            },
            message => _header.Text = message
        );

    // A step that changed replaces its line; a new one goes after the last step before the log.
    private void Upsert(TraceEntry step)
    {
        var at = _entries.FindIndex(e => e.Key == step.Key);
        if (at >= 0)
            _entries[at] = step;
        else
        {
            var log = _entries.FindIndex(e => e.Kind == TraceKind.Log);
            _entries.Insert(log < 0 ? _entries.Count : log, step);
        }
        Render();
    }

    private void Render()
    {
        _text.Clear();
        foreach (var entry in _entries)
            _text.Add(
                entry.Kind == TraceKind.Log
                    ? $"{"", 19}  {entry.Text}"
                    : $"{Lists.Wire.Time(entry.At)}  {entry.Text}"
            );
        _header.Text = Summary();
        if (_lines.SelectedItem is null && _text.Count > 0)
            _lines.SelectedItem = 0;
        SetNeedsDraw();
    }

    private string Summary() =>
        $"{_entries.Count(e => e.Kind == TraceKind.Step)} steps  ·  "
        + $"{_entries.Count(e => e.Kind == TraceKind.Decision)} decisions  ·  "
        + $"{_entries.Count(e => e.Kind == TraceKind.Log)} log lines  ·  live  ·  o source  esc close";

    private bool OnKey(Key key)
    {
        if (Ui.Typed(key) != "o" || _lines.SelectedItem is not { } index || index >= _entries.Count)
            return false;
        var entry = _entries[index];
        if (entry.Kind != TraceKind.Step || entry.Json?["name"]?.GetValue<string>() is not { } name)
            return false;
        CodeToOpen = name;
        RequestStop();
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stream?.Dispose();
            _stream = null;
        }
        base.Dispose(disposing);
    }
}
