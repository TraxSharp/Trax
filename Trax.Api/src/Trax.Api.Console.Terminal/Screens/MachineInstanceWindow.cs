using System.Collections.ObjectModel;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Machines;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>
/// One machine instance: where it is on its machine's graph, what it waits on, the runs it invoked and the history of
/// its transitions, read again a moment after the host says it changed (once for a burst of changes, and only the
/// latest read shown). The graph is drawn off the UI thread. v opens the trace of the run under the cursor.
/// </summary>
internal sealed class MachineInstanceWindow : Window
{
    private const int TransitionsShown = 100;

    private readonly ConsoleContext _context;
    private readonly MachinesResultOperationsMachines _machine;
    private readonly MachineInstancesAttentionResultOperationsMachineInstancesItems _instance;
    private readonly Label _header;
    private readonly ObservableCollection<string> _lines = [];
    private readonly KeyedList _list;
    private readonly List<long?> _runs = [];
    private IDisposable? _stream;
    private int _loading;
    private bool _pending;

    public MachineInstanceWindow(
        ConsoleContext context,
        MachinesResultOperationsMachines machine,
        MachineInstancesAttentionResultOperationsMachineInstancesItems instance
    )
    {
        _context = context;
        _machine = machine;
        _instance = instance;
        Title = $"{machine.Name} {instance.Id}";
        _header = new Label { Width = Dim.Fill(), Text = "Reading..." };
        _list = new KeyedList
        {
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Source = new ListWrapper<string>(_lines),
        };
        _list.Keys = OnKey;
        Add(_header, _list);
        // Read once the window runs, so every answer lands on the UI thread.
        IsRunningChanged += (_, e) =>
        {
            if (e.Value && _stream is null)
                Start();
        };
    }

    private void Start()
    {
        Load();
        _stream = _context.Client.OnMachineInstanceChanged(
            new OnMachineInstanceChangedVariables { Machine = _machine.Name },
            e =>
                _context.App.Invoke(() =>
                {
                    if (
                        e is TraxStreamEvent<OnMachineInstanceChangedResult>.Next next
                        && next.Data.OnMachineInstanceChanged.Id == _instance.Id
                        && next.Data.OnMachineInstanceChanged.OwnerKind == _instance.OwnerKind
                    )
                        Soon();
                    else if (e is TraxStreamEvent<OnMachineInstanceChangedResult>.Resumed)
                        Soon();
                })
        );
    }

    /// <summary>The run the operator asked to trace, read by whoever ran the window once it closed.</summary>
    public long? RunToTrace { get; private set; }

    /// <summary>The lines shown, in order.</summary>
    public IReadOnlyList<string> Lines => _lines;

    // Reads it again a moment after a change, once for a burst of them.
    private void Soon()
    {
        if (_pending)
            return;
        _pending = true;
        _context.App.AddTimeout(
            MainWindow.RefreshDelay,
            () =>
            {
                _pending = false;
                Load();
                return false;
            }
        );
    }

    private void Load()
    {
        var load = Interlocked.Increment(ref _loading);
        var vertical = _context.VerticalGraphs;
        Ui.Start(
            _context.App,
            async () =>
            {
                var detail = await _context.Client.MachineInstanceAsync(
                    new MachineInstanceVariables
                    {
                        Machine = _instance.Machine,
                        OwnerKind = _instance.OwnerKind,
                        Id = _instance.Id,
                        RowId = _instance.RowId,
                    }
                );
                var history = await _context.Client.MachineInstanceTransitionsAsync(
                    new MachineInstanceTransitionsVariables
                    {
                        Machine = _instance.Machine,
                        OwnerKind = _instance.OwnerKind,
                        Id = _instance.Id,
                        RowId = _instance.RowId,
                        First = TransitionsShown,
                    }
                );
                // Drawn here, off the UI thread; Show finds it drawn.
                if (detail.Data?.Operations?.MachineInstance is { } now)
                    MachineGraph.Lines(_machine, now.State, vertical);
                return (detail, history);
            },
            answers =>
            {
                // An answer to an older read, overtaken by a newer one, is dropped.
                if (load == Volatile.Read(ref _loading))
                    Show(answers.detail, answers.history);
            },
            message =>
            {
                if (load == Volatile.Read(ref _loading))
                    _header.Text = message;
            }
        );
    }

    private (
        TraxAnswer<MachineInstanceResult> Detail,
        TraxAnswer<MachineInstanceTransitionsResult> History
    )? _last;

    private void Show(
        TraxAnswer<MachineInstanceResult> detail,
        TraxAnswer<MachineInstanceTransitionsResult> history
    )
    {
        _last = (detail, history);
        if (detail.Data?.Operations?.MachineInstance is not { } now)
        {
            _header.Text = detail.Succeeded
                ? "The instance is gone."
                : $"Refused: {detail.Message}";
            return;
        }
        _header.Text =
            $"{now.State}  ·  version {now.Version}  ·  since {Wire.Time(now.InStateSince)}"
            + (now.DueAt is { } due ? $"  ·  timer due {Wire.Time(due)}" : "")
            + (
                now.Stuck ? "  ·  STUCK"
                : now.Overdue ? "  ·  overdue"
                : ""
            )
            + "  ·  j/k move  H/L sideways  r across/down  v trace a run  q close";
        _lines.Clear();
        _runs.Clear();
        void Line(string text, long? run = null)
        {
            _lines.Add(text);
            _runs.Add(run);
        }
        foreach (var line in MachineGraph.Lines(_machine, now.State, _context.VerticalGraphs))
            Line(line);
        if (now.WaitingOn is { } waiting)
        {
            Line("");
            Line(
                waiting.ExecutionId is { } run
                    ? $"Waits on run {run} ({(waiting.TrainState is { } s ? Wire.Name(s) : "?")})"
                    : $"Waits on queued entry {waiting.WorkQueueEntryId}",
                waiting.ExecutionId
            );
        }
        Line("");
        Line($"Runs it invoked{(now.IsInvokedRunsCapped ? " (the latest 50)" : "")}:");
        foreach (var run in now.InvokedRuns)
            Line(
                $"  {run.Id, 8}  {run.Name}  {Wire.Name(run.TrainState)}  {Wire.Time(run.StartTime)}"
                    + (run.IsLive ? "  live" : ""),
                run.Id
            );
        Line("");
        Line("History:");
        var transitions = history.Data?.Operations?.MachineInstance?.Transitions.Items;
        if (transitions is null)
            Line($"  {(history.Succeeded ? "none" : history.Message)}");
        else
            foreach (var t in transitions)
                Line(
                    $"  {Wire.Time(t.At)}  r{t.Revision}  {t.FromState ?? "(start)"} ──▶ {t.ToState}  "
                        + $"{Wire.Name(t.Kind).ToLowerInvariant()}"
                        + (t.Trigger is { } trigger ? $" {trigger}" : "")
                        + $" by {Wire.Name(t.Actor).ToLowerInvariant()}"
                        + (
                            t.StartedRun?.ExecutionId is { } started
                                ? $"  started run {started}"
                                : ""
                        ),
                    t.StartedRun?.ExecutionId
                );
        SetNeedsDraw();
    }

    private bool OnKey(Key key)
    {
        if (Ui.Typed(key) == "q")
        {
            RequestStop();
            return true;
        }
        if (Ui.Typed(key) == "r" && _last is { } last)
        {
            var vertical = _context.VerticalGraphs = !_context.VerticalGraphs;
            var state = last.Detail.Data?.Operations?.MachineInstance?.State;
            // Drawn off the UI thread, then shown with the latest answer, which may be newer than this one.
            Ui.Start(
                _context.App,
                () => Task.Run(() => MachineGraph.Lines(_machine, state, vertical)),
                _ =>
                {
                    if (_last is { } shown)
                        Show(shown.Detail, shown.History);
                },
                message => _header.Text = message
            );
            return true;
        }
        if (
            Ui.Typed(key) != "v"
            || _list.SelectedItem is not { } index
            || index >= _runs.Count
            || _runs[index] is not { } run
        )
            return false;
        RunToTrace = run;
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
