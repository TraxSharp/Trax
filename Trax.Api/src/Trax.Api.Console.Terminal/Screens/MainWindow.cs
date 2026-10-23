using System.Collections.ObjectModel;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>
/// The console's window: a tab per list and the machines, the live feed of run state changes, the status line, and
/// the streams that keep the lists current.
/// </summary>
internal sealed class MainWindow : Window
{
    /// <summary>How long after a change the lists it touches are read again, so a burst is one read.</summary>
    public static TimeSpan RefreshDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    private const int FeedLines = 5;

    private readonly ConsoleContext _context;
    private readonly Tabs _tabs;
    private readonly List<ListScreen> _lists = [];
    private readonly MachinesScreen _machines;
    private readonly ObservableCollection<string> _feed = [];
    private readonly Label _status;
    private readonly List<IDisposable> _streams = [];
    private readonly HashSet<ListScreen> _pending = [];

    // The scheduler's own trains, which the feed leaves out as the browser console's does.
    private readonly HashSet<string> _adminTrains = new(StringComparer.Ordinal);

    public MainWindow(ConsoleContext context, ConsoleState? state, string banner)
    {
        _context = context;
        Title = $"trax-console  ·  {context.Endpoint.GetLeftPart(UriPartial.Authority)}";
        // Below the tabs: the feed and its border, the status line, the status bar.
        _tabs = new Tabs { Width = Dim.Fill(), Height = Dim.Fill(FeedLines + 4) };
        foreach (var list in Catalog.All)
        {
            var screen = new ListScreen(context, list, state?.Lists.GetValueOrDefault(list.Name));
            _lists.Add(screen);
            _tabs.Add(screen);
        }
        _machines = new MachinesScreen(context);
        _tabs.Add(_machines);
        _tabs.ValueChanged += (_, _) => ShowHelp();

        var feed = new FrameView
        {
            Title = "Live",
            Y = Pos.Bottom(_tabs),
            Width = Dim.Fill(),
            Height = FeedLines + 2,
        };
        var feedList = new ListView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Source = new ListWrapper<string>(_feed),
            CanFocus = false,
        };
        feed.Add(feedList);

        _status = new Label
        {
            Y = Pos.Bottom(feed),
            Width = Dim.Fill(),
            Text = banner,
        };
        var bar = new StatusBar([
            new Shortcut(Key.F1, "Keys", ShowKeys),
            new Shortcut(Key.F5, "Refresh", RefreshAll),
            new Shortcut(Key.Q.WithCtrl, "Quit", () => _context.App.RequestStop()),
        ]);
        Add(_tabs, feed, _status, bar);

        context.Status = text => _status.Text = text;
        context.OpenTrace = OpenTrace;
        context.Tab = MoveTab;
        context.Quit = () => _context.App.RequestStop();

        if (state is not null && state.Tab < _lists.Count + 1)
            _tabs.Value = state.Tab < _lists.Count ? _lists[state.Tab] : _machines;
        // Nothing is read until the window runs: an answer handed to the UI thread before its loop exists would be
        // applied on whichever thread it arrived on, while the first frame draws.
        IsRunningChanged += (_, e) =>
        {
            if (!e.Value || _started)
                return;
            _started = true;
            _lists[0].SetFocus();
            _status.Text = $"{banner}  ·  {_lists[0].Help}";
            RefreshAll();
            _machines.Follow();
            // The feed starts once the host has named its own trains, so none of them slips in first.
            ReadAdminTrains(then: Follow);
        };
    }

    private bool _started;

    /// <summary>Where the window is, to open it again where it was.</summary>
    public ConsoleState State =>
        new(
            _tabs.Value is ListScreen screen ? _lists.IndexOf(screen)
                : _tabs.Value == _machines ? _lists.Count
                : 0,
            _lists.ToDictionary(l => l.List.Name, l => l.State)
        );

    /// <summary>The list screens, in tab order.</summary>
    public IReadOnlyList<ListScreen> Lists => _lists;

    /// <summary>The live feed's lines, newest last.</summary>
    public IReadOnlyList<string> Feed => _feed;

    // Every tab, in order: the lists, then the machines.
    private IReadOnlyList<View> TabViews => [.. _lists, _machines];

    private void MoveTab(int? by, int? to)
    {
        var tabs = TabViews;
        var at = Math.Max(0, tabs.ToList().IndexOf(_tabs.Value!));
        var next = to is { } number ? number - 1 : (at + (by ?? 0) + tabs.Count) % tabs.Count;
        if (next < 0 || next >= tabs.Count)
            return;
        _tabs.Value = tabs[next];
        tabs[next].SetFocus();
        ShowHelp();
    }

    private void ShowHelp() =>
        _status.Text = _tabs.Value switch
        {
            ListScreen screen => screen.Help,
            MachinesScreen machines => machines.Help,
            _ => "",
        };

    private void ShowKeys() =>
        _context.Prompts.Show(
            "Keys",
            [
                "Tabs           h / l, or 1 to 5",
                "Move           j / k, g / G for top and end, Ctrl+d / Ctrl+u half a page (or the arrows)",
                "Sideways       H / L, in a graph wider than the screen",
                "Pages          n next, p previous",
                "Filter         f (empty fields clear it)",
                "Mark           space; an action runs on the marked rows, or the current one",
                "Detail         enter",
                "Run trace      v, on a row that is a run",
                "Source         o, on a row that names a train, inside a checkout ($VISUAL or $EDITOR)",
                "All matching   b, an action on every row the filter matches, run by the host",
                "Refresh        F5",
                "Close, quit    q closes a trace or an instance, and quits the console (or Ctrl+Q)",
                "",
                .. Catalog.All.SelectMany(l =>
                    l.ActionInfos.Select(a => $"{l.Title, -14} {a.Key} {a.Name}: {a.Description}")
                ),
            ]
        );

    private void RefreshAll()
    {
        foreach (var screen in _lists)
            screen.Refresh();
        _machines.Refresh();
    }

    private void ReadAdminTrains(Action then) =>
        Ui.Start(
            _context.App,
            () => _context.Client.AdminTrainNamesAsync(),
            answer =>
            {
                _adminTrains.UnionWith(answer.Data?.Operations?.AdminTrainNames ?? []);
                then();
            },
            _ => then()
        );

    // Reads a list again a moment after the host says it changed, once for a burst of changes.
    private void Soon(ListScreen screen)
    {
        if (!_pending.Add(screen))
            return;
        _context.App.AddTimeout(
            RefreshDelay,
            () =>
            {
                _pending.Remove(screen);
                screen.Refresh();
                return false;
            }
        );
    }

    private void Follow()
    {
        var client = _context.Client;
        _streams.Add(
            client.OnDataChanged(e =>
                _context.App.Invoke(() =>
                {
                    switch (e)
                    {
                        case TraxStreamEvent<OnDataChangedResult>.Next next:
                            foreach (
                                var screen in _lists.Where(l =>
                                    l.List.RefreshOn == next.Data.OnDataChanged.Domain
                                )
                            )
                                Soon(screen);
                            break;
                        case TraxStreamEvent<OnDataChangedResult>.Resumed:
                            RefreshAll();
                            break;
                        default:
                            OnStreamTrouble(e);
                            break;
                    }
                })
            )
        );
        _streams.Add(
            client.OnTrainStateChanged(e =>
                _context.App.Invoke(() =>
                {
                    if (e is not TraxStreamEvent<OnTrainStateChangedResult>.Next next)
                    {
                        OnStreamTrouble(e);
                        return;
                    }
                    var change = next.Data.OnTrainStateChanged;
                    if (_adminTrains.Contains(change.TrainName))
                        return;
                    _feed.Add(
                        $"{Wire.Time(change.Timestamp)}  {change.MetadataId, 8}  {change.TrainName}  "
                            + $"{Wire.Name(change.TrainState)}"
                            + (
                                change.FailureReason is { } reason
                                    ? $"  {change.FailureJunction}: {reason}"
                                    : ""
                            )
                    );
                    while (_feed.Count > 100)
                        _feed.RemoveAt(0);
                    Soon(_lists[0]);
                })
            )
        );
    }

    private void OnStreamTrouble<T>(TraxStreamEvent<T> e)
    {
        switch (e)
        {
            case TraxStreamEvent<T>.Interrupted interrupted:
                _status.Text = $"Live updates interrupted ({interrupted.Reason}); reconnecting.";
                break;
            case TraxStreamEvent<T>.Ended ended:
                _status.Text =
                    "Live updates stopped: "
                    + (
                        ended.Failure?.Message
                        ?? string.Join("; ", ended.Errors.Select(x => x.Message))
                    )
                    + ". F5 reads the lists again.";
                break;
        }
    }

    private void OpenTrace(long run)
    {
        var window = new RunTraceWindow(_context, run);
        _context.App.Run(window);
        var code = window.CodeToOpen;
        window.Dispose();
        if (code is not null)
            _context.OpenCode(code);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var stream in _streams)
                stream.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Where the console was: its tab and each list's page and filters.</summary>
internal sealed record ConsoleState(int Tab, IReadOnlyDictionary<string, ListState> Lists);
