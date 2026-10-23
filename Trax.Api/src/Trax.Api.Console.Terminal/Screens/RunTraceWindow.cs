using System.Collections.ObjectModel;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Trace;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>
/// One run as a timeline: its steps and decisions as they happened, then its log, with the steps followed live while
/// the window is open. The steps are followed before the trace is read, and a step that arrives while it is read is
/// kept if it is newer than what the read answers, so neither order loses a change. Each part is read up to
/// <see cref="RunTrace.Take"/>; a part that filled it is named on a line under the header, since the run may hold
/// more. Esc closes it; o opens the source of the step under the cursor.
/// </summary>
internal sealed class RunTraceWindow : Window
{
    private readonly ConsoleContext _context;
    private readonly long _run;
    private readonly Label _header;
    private readonly Label _cut;
    private readonly KeyedList _lines;
    private readonly ObservableCollection<string> _text = [];
    private readonly List<TraceEntry> _entries = [];

    // The steps that arrived since the oldest read still out began, merged into its answer.
    private readonly Dictionary<string, TraceEntry> _arrived = new(StringComparer.Ordinal);
    private IDisposable? _stream;
    private int _loading;
    private bool _reading;

    public RunTraceWindow(ConsoleContext context, long run)
    {
        _context = context;
        _run = run;
        Title = $"Run {run}";
        _header = new Label { Width = Dim.Fill(), Text = "Reading..." };
        _cut = new Label
        {
            Y = 1,
            Width = Dim.Fill(),
            Visible = false,
        };
        _lines = new KeyedList
        {
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Source = new ListWrapper<string>(_text),
        };
        _lines.Keys = OnKey;
        Add(_header, _cut, _lines);
        // Read once the window runs, so every answer lands on the UI thread.
        IsRunningChanged += (_, e) =>
        {
            if (!e.Value || _stream is not null)
                return;
            _lines.SetFocus();
            _stream = RunTrace.Follow(
                context.Client,
                run,
                step => context.App.Invoke(() => Upsert(step)),
                () => context.App.Invoke(Load),
                reason =>
                    context.App.Invoke(() =>
                        _header.Text = $"{Summary()}  ·  not live: {TerminalText.Line(reason)}"
                    )
            );
            Load();
        };
    }

    /// <summary>The type the operator asked to open, read by whoever ran the window once it closed.</summary>
    public string? CodeToOpen { get; private set; }

    /// <summary>The lines shown, in order.</summary>
    public IReadOnlyList<TraceEntry> Entries => _entries;

    /// <summary>The line naming the parts the last read cut at <see cref="RunTrace.Take"/>; null when none was.</summary>
    public string? Cut => _cut.Visible ? _cut.Text : null;

    private void Load()
    {
        var load = Interlocked.Increment(ref _loading);
        _reading = true;
        Ui.Start(
            _context.App,
            () => RunTrace.LoadAsync(_context.Client, _run, CancellationToken.None),
            answer =>
            {
                // An answer to an older read, overtaken by a newer one, is dropped; the newer one's merge covers it.
                if (load != Volatile.Read(ref _loading))
                    return;
                _reading = false;
                var arrived = _arrived.Values.ToList();
                _arrived.Clear();
                if (answer.Data is not { } entries)
                {
                    _header.Text = $"Refused: {TerminalText.Line(answer.Message)}";
                    return;
                }
                _entries.Clear();
                _entries.AddRange(entries);
                ShowCut(entries);
                foreach (var step in arrived)
                    Place(step);
                Render();
            },
            message =>
            {
                if (load != Volatile.Read(ref _loading))
                    return;
                _reading = false;
                _arrived.Clear();
                _header.Text = TerminalText.Line(message);
            }
        );
    }

    // The parts of the read that filled RunTrace.Take, named under the header; the steps that arrive live are not
    // counted, since the read is what was cut.
    private void ShowCut(IReadOnlyList<TraceEntry> read)
    {
        var cut = new[]
        {
            (TraceKind.Step, "steps"),
            (TraceKind.Decision, "decisions"),
            (TraceKind.Log, "log lines"),
        }
            .Where(part => read.Count(e => e.Kind == part.Item1) >= RunTrace.Take)
            .Select(part => part.Item2)
            .ToArray();
        _cut.Visible = cut.Length > 0;
        _cut.Text = cut switch
        {
            [] => "",
            [var one] => $"Only the first {RunTrace.Take} {one} were read; the run may have more.",
            _ =>
                $"Only the first {RunTrace.Take} {string.Join(", ", cut[..^1])} and {cut[^1]} were read; the run may have more.",
        };
        _lines.Y = cut.Length > 0 ? 2 : 1;
    }

    private void Upsert(TraceEntry step)
    {
        if (_reading)
            _arrived[step.Key] = _arrived.TryGetValue(step.Key, out var seen)
                ? TraceEntry.Newest(seen, step)
                : step;
        Place(step);
        Render();
    }

    // A step that changed replaces its line unless the line is newer; a new one goes after the last step before the
    // log.
    private void Place(TraceEntry step)
    {
        var at = _entries.FindIndex(e => e.Key == step.Key);
        if (at >= 0)
            _entries[at] = TraceEntry.Newest(_entries[at], step);
        else
        {
            var log = _entries.FindIndex(e => e.Kind == TraceKind.Log);
            _entries.Insert(log < 0 ? _entries.Count : log, step);
        }
    }

    private void Render()
    {
        _text.Clear();
        foreach (var entry in _entries)
            _text.Add(
                TerminalText.Line(
                    entry.Kind == TraceKind.Log
                        ? $"{"", 19}  {entry.Text}"
                        : $"{Wire.Time(entry.At)}  {entry.Text}"
                )
            );
        _header.Text = Summary();
        if (_lines.SelectedItem is null && _text.Count > 0)
            _lines.SelectedItem = 0;
        SetNeedsDraw();
    }

    private string Summary() =>
        $"{_entries.Count(e => e.Kind == TraceKind.Step)} steps  ·  "
        + $"{_entries.Count(e => e.Kind == TraceKind.Decision)} decisions  ·  "
        + $"{_entries.Count(e => e.Kind == TraceKind.Log)} log lines  ·  live  ·  j/k move  o source  q close";

    private bool OnKey(Key key)
    {
        if (Ui.Typed(key) == "q")
        {
            RequestStop();
            return true;
        }
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
