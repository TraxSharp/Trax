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
/// The machines the host registers, read only: each one's graph, and its instances, the stuck ones first when asked.
/// An instance opens with its state on the graph and its history. Sending a machine a command is not here: the
/// operations surface does not offer it.
/// </summary>
internal sealed class MachinesScreen : View
{
    private const int InstancesShown = 50;

    private readonly ConsoleContext _context;
    private readonly KeyedList _machineList;
    private readonly ObservableCollection<string> _machineNames = [];
    private readonly ObservableCollection<string> _graph = [];
    private readonly ObservableCollection<string> _instanceLines = [];
    private readonly KeyedList _instanceList;
    private readonly Label _instancesHeader;
    private IReadOnlyList<MachinesResultOperationsMachines> _machines = [];
    private IReadOnlyList<MachineInstancesAttentionResultOperationsMachineInstancesItems> _instances =
    [];
    private bool _stuckOnly;
    private IDisposable? _stream;
    private bool _pending;

    public MachinesScreen(ConsoleContext context)
    {
        _context = context;
        Title = "Machines";
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        _machineList = new KeyedList
        {
            Width = 24,
            Height = Dim.Fill(),
            Source = new ListWrapper<string>(_machineNames),
        };
        _machineList.ValueChanged += (_, _) => ShowMachine();
        var graph = new FrameView
        {
            Title = "Graph",
            X = Pos.Right(_machineList) + 1,
            Width = Dim.Fill(),
            Height = Dim.Percent(60),
        };
        graph.Add(
            new KeyedList
            {
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                Source = new ListWrapper<string>(_graph),
            }
        );
        _instancesHeader = new Label
        {
            X = Pos.Right(_machineList) + 1,
            Y = Pos.Bottom(graph),
            Width = Dim.Fill(),
        };
        _instanceList = new KeyedList
        {
            X = Pos.Right(_machineList) + 1,
            Y = Pos.Bottom(_instancesHeader),
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Source = new ListWrapper<string>(_instanceLines),
        };
        _machineList.Keys = key => OnKey(_machineList, key);
        _instanceList.Keys = key => OnKey(_instanceList, key);
        Add(_machineList, graph, _instancesHeader, _instanceList);
    }

    /// <summary>Follows instance changes, to read the selected machine's instances again when one of them changes.</summary>
    public void Follow() =>
        _stream ??= _context.Client.OnMachineInstanceChanged(
            new OnMachineInstanceChangedVariables(),
            e =>
                _context.App.Invoke(() =>
                {
                    if (
                        e is TraxStreamEvent<OnMachineInstanceChangedResult>.Next next
                        && next.Data.OnMachineInstanceChanged.Machine == Selected?.Name
                    )
                        Soon();
                    else if (e is TraxStreamEvent<OnMachineInstanceChangedResult>.Resumed)
                        Soon();
                })
        );

    /// <summary>The keys this screen takes, for the status line.</summary>
    public string Help =>
        $"{VimKeys.Help}  tab machines/graph/instances  H/L scroll the graph  r across/down  enter open  s stuck only";

    /// <summary>The machine selected now.</summary>
    public MachinesResultOperationsMachines? Selected =>
        _machineList.SelectedItem is { } i && i < _machines.Count ? _machines[i] : null;

    /// <summary>The instances shown now.</summary>
    public IReadOnlyList<MachineInstancesAttentionResultOperationsMachineInstancesItems> Instances =>
        _instances;

    /// <summary>Reads the machines again, then the selected one's instances.</summary>
    public void Refresh() =>
        Ui.Start(
            _context.App,
            () => _context.Client.MachinesAsync(),
            answer =>
            {
                if (answer.Data?.Operations?.Machines is not { } machines)
                {
                    _instancesHeader.Text = $"Refused: {answer.Message ?? "no machines namespace"}";
                    return;
                }
                var selected = Selected?.Name;
                _machines = machines.Where(m => !m.SystemOwned).OrderBy(m => m.Name).ToList();
                _machineNames.Clear();
                foreach (var machine in _machines)
                    _machineNames.Add($"{machine.Name} v{machine.Version}");
                if (_machines.Count == 0)
                {
                    _graph.Clear();
                    _instancesHeader.Text = "This host serves no machines.";
                    return;
                }
                var at = Math.Max(0, _machines.ToList().FindIndex(m => m.Name == selected));
                _machineList.SelectedItem = at;
                ShowMachine();
            },
            message => _instancesHeader.Text = message
        );

    private void ShowMachine()
    {
        ShowGraph();
        LoadInstances();
    }

    private void ShowGraph()
    {
        if (Selected is not { } machine)
            return;
        _graph.Clear();
        foreach (var line in MachineGraph.Lines(machine, vertical: _context.VerticalGraphs))
            _graph.Add(line);
    }

    private void LoadInstances()
    {
        if (Selected is not { } machine)
            return;
        var stuck = _stuckOnly;
        Ui.Start(
            _context.App,
            () =>
                _context.Client.MachineInstancesAttentionAsync(
                    new MachineInstancesAttentionVariables
                    {
                        Machine = machine.Name,
                        Stuck = stuck ? true : default(Optional<bool?>),
                        Skip = 0,
                        Take = InstancesShown,
                    }
                ),
            answer =>
            {
                if (answer.Data?.Operations?.MachineInstances is not { } page)
                {
                    _instancesHeader.Text = $"Refused: {answer.Message}";
                    return;
                }
                _instances = page.Items;
                _instanceLines.Clear();
                foreach (var i in page.Items)
                    _instanceLines.Add(
                        $"{i.State, -20} {Wire.Name(i.OwnerKind).ToLowerInvariant(), -6} {i.Id}  since {Wire.Time(i.InStateSince)}"
                            + (
                                i.Stuck ? "  STUCK"
                                : i.Overdue ? "  overdue"
                                : ""
                            )
                            + (i.WaitingOn?.ExecutionId is { } run ? $"  waits on run {run}" : "")
                    );
                _instancesHeader.Text =
                    $"{page.TotalCount}{(page.IsCountCapped ? "+" : "")} instances{(stuck ? " stuck" : "")}"
                    + (page.TotalCount > InstancesShown ? $", the first {InstancesShown}" : "");
            },
            message => _instancesHeader.Text = message
        );
    }

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
                LoadInstances();
                return false;
            }
        );
    }

    private bool OnKey(View from, Key key)
    {
        if (VimKeys.Window(_context, key))
            return true;
        if (Ui.Typed(key) == "r")
        {
            _context.VerticalGraphs = !_context.VerticalGraphs;
            ShowGraph();
            return true;
        }
        if (Ui.Typed(key) == "s")
        {
            _stuckOnly = !_stuckOnly;
            LoadInstances();
            return true;
        }
        if (
            key != Key.Enter
            || from != _instanceList
            || _instanceList.SelectedItem is not { } index
            || index >= _instances.Count
            || Selected is not { } machine
        )
            return false;
        var window = new MachineInstanceWindow(_context, machine, _instances[index]);
        _context.App.Run(window);
        var run = window.RunToTrace;
        window.Dispose();
        if (run is { } id)
            _context.OpenTrace(id);
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
