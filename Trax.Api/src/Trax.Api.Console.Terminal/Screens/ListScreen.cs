using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Trax.Api.Console.Terminal.Lists;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>
/// One list of the console in a tab: a page of rows read with keyset paging, the filters it is read with, the rows
/// marked for an action, and the keys that act on them.
/// </summary>
internal sealed class ListScreen : View
{
    /// <summary>Rows read per page, as the browser console reads them.</summary>
    public const int PageSize = 25;

    private readonly ConsoleContext _context;
    private readonly Label _header;
    private readonly TableView _table;
    private readonly HashSet<long> _marked = [];
    private readonly Stack<long?> _back = new();
    private Dictionary<string, string> _filters = new(StringComparer.Ordinal);
    private long? _after;
    private ListPage<ConsoleRow>? _page;
    private int _loading;

    public ListScreen(ConsoleContext context, IConsoleList list, ListState? state = null)
    {
        _context = context;
        List = list;
        Title = list.Title;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        if (state is not null)
        {
            _filters = new(state.Filters, StringComparer.Ordinal);
            _after = state.After;
            foreach (var back in state.Back.Reverse())
                _back.Push(back);
        }

        _header = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Text = "Reading...",
        };
        _table = new TableView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            FullRowSelect = true,
            // Letters are this screen's keys, not a search.
            CollectionNavigator = null,
        };
        _table.KeyDown += OnKey;
        Add(_header, _table);
    }

    /// <summary>The list this screen shows.</summary>
    public IConsoleList List { get; }

    /// <summary>The rows shown now.</summary>
    public IReadOnlyList<ConsoleRow> Rows => _page?.Rows ?? [];

    /// <summary>The ids marked for an action.</summary>
    public IReadOnlySet<long> Marked => _marked;

    /// <summary>The filters it is read with.</summary>
    public IReadOnlyDictionary<string, string> Filters => _filters;

    /// <summary>Where it is, to open it again where it was after the editor ran.</summary>
    public ListState State => new(new Dictionary<string, string>(_filters), _after, _back.ToList());

    /// <summary>The keys this screen takes, for the status line.</summary>
    public string Help =>
        string.Join(
            "  ",
            new[]
            {
                "n/p page",
                "f filter",
                "space mark",
                List.HasDetail ? "enter detail" : null,
                Rows.Any(r => r.Run is not null) ? "v trace" : null,
                Rows.Any(r => r.Code is not null) ? "o code" : null,
            }
                .Concat(List.ActionInfos.Select(a => $"{a.Key} {a.Name}"))
                .Append(List.BulkActions.Count > 0 ? "b all matching" : null)
                .Where(h => h is not null)
        );

    /// <summary>Reads the page it is on again: after an action, or when the host says the list changed.</summary>
    public void Refresh()
    {
        var load = Interlocked.Increment(ref _loading);
        var query = new ListQuery(PageSize, _after, new Dictionary<string, string>(_filters));
        Ui.Start(
            _context.App,
            () => List.LoadAsync(_context.Client, query, CancellationToken.None),
            answer =>
            {
                // An answer to an older read, overtaken by a newer one, is dropped.
                if (load != Volatile.Read(ref _loading))
                    return;
                if (answer.Data is not { } page)
                {
                    _header.Text = $"Refused: {answer.Message}";
                    return;
                }
                Show(page);
            },
            message => _header.Text = message
        );
    }

    private void Show(ListPage<ConsoleRow> page)
    {
        var selected = _table.Value?.SelectedCell.Y ?? 0;
        _page = page;
        _marked.IntersectWith(page.Rows.Select(r => r.Id));
        _table.Table = new RowsSource(List.ColumnNames, page.Rows, _marked);
        if (page.Rows.Count > 0)
            _table.SetSelection(0, Math.Clamp(selected, 0, page.Rows.Count - 1), false);
        _table.Update();
        var filters =
            _filters.Count == 0
                ? "no filter"
                : string.Join(", ", _filters.Select(f => $"{f.Key}={f.Value}"));
        _header.Text =
            $"page {_back.Count + 1}  ·  {(page.Estimated ? "about " : "")}{page.TotalCount} in all  ·  {filters}"
            + (_marked.Count > 0 ? $"  ·  {_marked.Count} marked" : "");
        SetNeedsDraw();
    }

    private ConsoleRow? Current =>
        _table.Value?.SelectedCell.Y is { } y && y >= 0 && y < Rows.Count ? Rows[y] : null;

    // The marked rows, or the current one when none is marked.
    private IReadOnlyList<long> Targets =>
        _marked.Count > 0 ? _marked.Order().ToList()
        : Current is { } row ? [row.Id]
        : [];

    private void OnKey(object? sender, Key key)
    {
        if (key == Key.Enter)
            Handle(key, Detail);
        else if (key == Key.Space)
            Handle(key, Mark);
        else if (key == Key.F5)
            Handle(key, Refresh);
        else if (Ui.Typed(key) is { } typed)
            switch (typed)
            {
                case "n":
                    Handle(key, NextPage);
                    break;
                case "p":
                    Handle(key, PreviousPage);
                    break;
                case "f":
                    Handle(key, Filter);
                    break;
                case "v" when Current?.Run is { } run:
                    Handle(key, () => _context.OpenTrace(run));
                    break;
                case "o" when Current?.Code is { } code:
                    Handle(key, () => _context.OpenCode(code));
                    break;
                case "b" when List.BulkActions.Count > 0:
                    Handle(key, Bulk);
                    break;
                default:
                    if (
                        List.ActionInfos.FirstOrDefault(a => a.Key.ToString() == typed) is
                        { } action
                    )
                        Handle(key, () => Act(action));
                    break;
            }

        static void Handle(Key key, Action action)
        {
            key.Handled = true;
            action();
        }
    }

    private void NextPage()
    {
        if (_page?.NextCursor is not { } next)
        {
            _context.Status("This is the last page.");
            return;
        }
        _back.Push(_after);
        _after = next;
        Refresh();
    }

    private void PreviousPage()
    {
        if (_back.Count == 0)
        {
            _context.Status("This is the first page.");
            return;
        }
        _after = _back.Pop();
        Refresh();
    }

    private void Mark()
    {
        if (Current is not { } row)
            return;
        if (!_marked.Remove(row.Id))
            _marked.Add(row.Id);
        if (_page is { } page)
            Show(page);
        _table.MoveCursorByOffset(0, 1, false, null);
    }

    private void Filter()
    {
        var values = _context.Prompts.Form(
            $"Filter {List.Title.ToLowerInvariant()}",
            List.Filters.Select(f =>
                    (
                        f.Choices is null ? f.Name : $"{f.Name} ({string.Join("|", f.Choices)})",
                        _filters.GetValueOrDefault(f.Name, "")
                    )
                )
                .ToList(),
            accept: "Apply",
            clear: "Clear"
        );
        if (values is null)
            return;
        _filters = List
            .Filters.Zip(values)
            .Where(p => p.Second.Trim().Length > 0)
            .ToDictionary(p => p.First.Name, p => p.Second.Trim(), StringComparer.Ordinal);
        _after = null;
        _back.Clear();
        _marked.Clear();
        Refresh();
    }

    private void Detail()
    {
        if (!List.HasDetail || Current is not { } row)
            return;
        Ui.Start(
            _context.App,
            () => List.DetailAsync(_context.Client, row.Id, CancellationToken.None),
            answer =>
            {
                if (answer.Data is not { } fields)
                {
                    _context.Prompts.Tell("Refused", answer.Message ?? "The host refused it.");
                    return;
                }
                var width = fields.Count == 0 ? 0 : fields.Max(f => f.Label.Length);
                _context.Prompts.Show(
                    $"{List.Title} {row.Id}",
                    fields.Select(f => $"{f.Label.PadRight(width)}  {f.Value}")
                );
            },
            message => _context.Status(message)
        );
    }

    private void Act(ActionInfo action)
    {
        var ids = Targets;
        if (ids.Count == 0)
            return;
        var what =
            ids.Count == 1
                ? $"{List.Title.ToLowerInvariant()} {ids[0]}"
                : $"{ids.Count} {List.Title.ToLowerInvariant()}";
        var options = new ActionOptions();
        if (action.AsksAfresh)
        {
            var chosen = _context.Prompts.Ask(
                Capital(action.Name),
                $"{Capital(action.Name)} {what}? A run that asks decisions may replay its answers or ask afresh.",
                "Replay answers",
                "Ask afresh",
                "Cancel"
            );
            if (chosen is not (0 or 1))
                return;
            options = options with { AskAfresh = chosen == 1 };
        }
        else if (action.Confirm)
        {
            if (
                _context.Prompts.Ask(
                    Capital(action.Name),
                    $"{Capital(action.Name)} {what}?",
                    Capital(action.Name),
                    "Cancel"
                ) != 0
            )
                return;
        }
        if (action.NeedsNote)
        {
            var note = _context.Prompts.Form(
                Capital(action.Name),
                [("Note", "")],
                accept: Capital(action.Name)
            );
            if (note is not [var text] || text.Trim().Length == 0)
                return;
            options = options with { Note = text.Trim() };
        }
        _context.Status($"{Capital(action.Name)}: sending...");
        Ui.Start(
            _context.App,
            () => List.RunAsync(_context.Client, action.Name, ids, options, CancellationToken.None),
            outcome =>
            {
                _context.Status(
                    outcome.Succeeded
                        ? $"{Capital(action.Name)}: {outcome.Message}"
                        : $"{Capital(action.Name)} refused: {outcome.Message}"
                );
                _marked.Clear();
                Refresh();
            },
            message => _context.Status(message)
        );
    }

    private void Bulk()
    {
        if (_page is not { } page)
            return;
        if (page.Estimated)
        {
            _context.Prompts.Tell(
                "All matching",
                "The count is an estimate. Filter the list until it is exact, so what you confirm is what runs."
            );
            return;
        }
        var actions = List.BulkActions;
        var filters =
            _filters.Count == 0
                ? "every row"
                : string.Join(", ", _filters.Select(f => $"{f.Key}={f.Value}"));
        var chosen = _context.Prompts.Ask(
            "All matching",
            $"Apply to all {page.TotalCount} {List.Title.ToLowerInvariant()} matching {filters}. The host refuses if the filter now matches another count.",
            [.. actions.Select(a => Capital(a.Name)), "Cancel"]
        );
        if (chosen is not { } index || index >= actions.Count)
            return;
        var bulk = actions[index];
        var options = new ActionOptions();
        if (bulk.AsksAfresh)
        {
            var afresh = _context.Prompts.Ask(
                Capital(bulk.Name),
                "Replay recorded answers, or ask afresh?",
                "Replay answers",
                "Ask afresh",
                "Cancel"
            );
            if (afresh is not (0 or 1))
                return;
            options = options with { AskAfresh = afresh == 1 };
        }
        if (bulk.NeedsNote)
        {
            var note = _context.Prompts.Form(
                Capital(bulk.Name),
                [("Note", "")],
                accept: Capital(bulk.Name)
            );
            if (note is not [var text] || text.Trim().Length == 0)
                return;
            options = options with { Note = text.Trim() };
        }
        var filterSnapshot = new Dictionary<string, string>(_filters);
        _context.Status($"{Capital(bulk.Name)} all {page.TotalCount}: starting...");
        Ui.Start(
            _context.App,
            () =>
                Catalog.StartBulkAsync(
                    _context.Client,
                    bulk,
                    filterSnapshot,
                    page.TotalCount,
                    options,
                    CancellationToken.None
                ),
            started =>
            {
                _context.Status(
                    started.Outcome.Succeeded
                        ? $"{Capital(bulk.Name)} all {page.TotalCount}: {started.Outcome.Message}"
                        : $"{Capital(bulk.Name)} refused: {started.Outcome.Message}"
                );
                if (started.Operation is { } operation)
                    BulkProgress.Follow(_context, operation, Refresh);
            },
            message => _context.Status(message)
        );
    }

    private static string Capital(string name) => char.ToUpperInvariant(name[0]) + name[1..];

    // The page as the table reads it: a mark column, then the list's columns.
    private sealed class RowsSource(
        IReadOnlyList<string> columns,
        IReadOnlyList<ConsoleRow> rows,
        IReadOnlySet<long> marked
    ) : ITableSource
    {
        public string[] ColumnNames { get; } = [" ", .. columns];

        public int Columns => ColumnNames.Length;

        public int Rows => rows.Count;

        public object this[int row, int col] =>
            col == 0 ? (marked.Contains(rows[row].Id) ? "*" : " ") : rows[row].Cells[col - 1];
    }
}

/// <summary>Where a list screen is: its filters, the page it is on and the pages before it.</summary>
internal sealed record ListState(
    IReadOnlyDictionary<string, string> Filters,
    long? After,
    IReadOnlyList<long?> Back
);
