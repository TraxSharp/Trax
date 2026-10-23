using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Lists;

/// <summary>One page of a list: its rows, the total, and the cursor of the next page.</summary>
internal sealed record ListPage<TRow>(
    IReadOnlyList<TRow> Rows,
    int TotalCount,
    bool Estimated,
    long? NextCursor
);

/// <summary>Builds a <see cref="ListPage{TRow}"/> with its row type inferred.</summary>
internal static class ListPage
{
    public static ListPage<TRow> Of<TRow>(
        IReadOnlyList<TRow> rows,
        int totalCount,
        bool estimated,
        long? nextCursor
    ) => new(rows, totalCount, estimated, nextCursor);
}

/// <summary>What a list is asked for: a page size, the cursor to start after, and the filters by name.</summary>
internal sealed record ListQuery(int Take, long? After, IReadOnlyDictionary<string, string> Filters)
{
    public static ListQuery First(int take = 25) =>
        new(take, null, new Dictionary<string, string>(StringComparer.Ordinal));
}

/// <summary>A filter a list takes, by the name its query variable has.</summary>
/// <param name="Name">The filter's name on the command line (<c>--where state=FAILED</c>) and in the filter dialog.</param>
/// <param name="Description">What it filters by, for the filter dialog and --help.</param>
/// <param name="Choices">The values an enum filter takes, as the host writes them; null for free text.</param>
internal sealed record ListFilter(
    string Name,
    string Description,
    IReadOnlyList<string>? Choices = null
);

/// <summary>A column of a list as the terminal shows it.</summary>
internal sealed record ListColumn<TRow>(string Name, Func<TRow, string> Value);

/// <summary>What an action did: the host's own words, or why it was refused.</summary>
internal sealed record ActionOutcome(bool Succeeded, string Message, string? Code = null)
{
    public static ActionOutcome Of<T>(TraxAnswer<T> answer, Func<T, (bool Ok, string Message)> read)
    {
        if (!answer.Succeeded)
            return new ActionOutcome(false, answer.Message ?? "The host refused it.", answer.Code);
        var (ok, message) = read(answer.Data!);
        return new ActionOutcome(ok, message);
    }
}

/// <summary>
/// Something an operator does to rows of a list, through one of the console's documents. The host checks it as it
/// checks the browser console's: the operations gate decides, and the answer says what happened.
/// </summary>
/// <param name="Name">The action's name on the command line (<c>do dead-letters requeue 7</c>).</param>
/// <param name="Key">The key that runs it on the selected rows in the terminal.</param>
/// <param name="Description">What it does, for the help line.</param>
/// <param name="NeedsNote">Whether it asks for a note, which the host records with it.</param>
/// <param name="Confirm">Whether the terminal asks before running it.</param>
/// <param name="Run">Sends it for the rows' ids.</param>
/// <param name="AsksAfresh">Whether the run it queues may ask its decisions afresh rather than replay them.</param>
internal sealed record ListAction<TRow>(
    string Name,
    char Key,
    string Description,
    bool NeedsNote,
    bool Confirm,
    Func<
        TraxClient,
        IReadOnlyList<long>,
        ActionOptions,
        CancellationToken,
        Task<ActionOutcome>
    > Run,
    bool AsksAfresh = false
);

/// <summary>What an action is run with besides the rows.</summary>
internal sealed record ActionOptions(string? Note = null, bool AskAfresh = false);

/// <summary>
/// "Apply to all matching": an action run by the host over every row a filter matches, as a bulk operation
/// (<c>StartBulkOperation</c>), confirmed against the count the terminal shows.
/// </summary>
internal sealed record BulkAction(
    string Name,
    BulkOperationKind Kind,
    bool NeedsNote,
    Func<IReadOnlyDictionary<string, string>, BulkOperationFilterInput> Filter,
    bool AsksAfresh = false
);

/// <summary>A row as the terminal and the headless mode use it, whatever list it came from.</summary>
/// <param name="Id">The id its actions take.</param>
/// <param name="Cells">Its columns' text, in column order.</param>
/// <param name="Json">The row as the host wrote it.</param>
/// <param name="Run">The run it is about, for the run trace; null when none.</param>
/// <param name="Code">The type a checkout may hold the source of (a train), for jump to code; null when none.</param>
internal sealed record ConsoleRow(
    long Id,
    IReadOnlyList<string> Cells,
    JsonNode? Json,
    long? Run,
    string? Code
);

/// <summary>A list without its row type: what the terminal's screens and the headless commands work with.</summary>
internal interface IConsoleList
{
    string Name { get; }
    string Title { get; }
    IReadOnlyList<string> ColumnNames { get; }
    IReadOnlyList<ListFilter> Filters { get; }
    IReadOnlyList<ActionInfo> ActionInfos { get; }
    IReadOnlyList<BulkAction> BulkActions { get; }
    ChangeDomain? RefreshOn { get; }
    bool HasDetail { get; }

    /// <summary>One page, its rows as <see cref="ConsoleRow"/>s.</summary>
    Task<TraxAnswer<ListPage<ConsoleRow>>> LoadAsync(
        TraxClient client,
        ListQuery query,
        CancellationToken cancellationToken
    );

    /// <summary>Runs the action named <paramref name="action"/> on <paramref name="ids"/>.</summary>
    /// <exception cref="InvalidOperationException">The list has no action of that name.</exception>
    Task<ActionOutcome> RunAsync(
        TraxClient client,
        string action,
        IReadOnlyList<long> ids,
        ActionOptions options,
        CancellationToken cancellationToken
    );

    /// <summary>A row's detail as label and value pairs.</summary>
    Task<TraxAnswer<IReadOnlyList<(string Label, string Value)>>> DetailAsync(
        TraxClient client,
        long id,
        CancellationToken cancellationToken
    );
}

/// <summary>An action without the code that runs it: its name, key and what it asks for.</summary>
internal sealed record ActionInfo(
    string Name,
    char Key,
    string Description,
    bool NeedsNote,
    bool Confirm,
    bool AsksAfresh
);

/// <summary>
/// A list the console shows and the headless mode prints: how to read a page through the generated client, its
/// columns, its filters and the actions on its rows.
/// </summary>
internal sealed class ConsoleList<TRow> : IConsoleList
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<ListColumn<TRow>> Columns { get; init; }
    public required Func<TRow, long> IdOf { get; init; }
    public required Func<
        TraxClient,
        ListQuery,
        CancellationToken,
        Task<TraxAnswer<ListPage<TRow>>>
    > Load { get; init; }
    public IReadOnlyList<ListFilter> Filters { get; init; } = [];
    public IReadOnlyList<ListAction<TRow>> Actions { get; init; } = [];
    public IReadOnlyList<BulkAction> BulkActions { get; init; } = [];

    /// <summary>The domain whose <c>onDataChanged</c> means this list should be read again.</summary>
    public ChangeDomain? RefreshOn { get; init; }

    /// <summary>The row's detail, as label and value pairs, read on demand; null when the list has none.</summary>
    public Func<
        TraxClient,
        long,
        CancellationToken,
        Task<TraxAnswer<IReadOnlyList<(string Label, string Value)>>>
    >? Detail { get; init; }

    /// <summary>The run a row is about, for the run trace; null when a row is not a run.</summary>
    public Func<TRow, long?>? RunOf { get; init; }

    /// <summary>The type a row names that a checkout may hold the source of (a train), for jump to code.</summary>
    public Func<TRow, string?>? CodeOf { get; init; }

    public IReadOnlyList<string> ColumnNames => Columns.Select(c => c.Name).ToList();

    public IReadOnlyList<ActionInfo> ActionInfos =>
        Actions
            .Select(a => new ActionInfo(
                a.Name,
                a.Key,
                a.Description,
                a.NeedsNote,
                a.Confirm,
                a.AsksAfresh
            ))
            .ToList();

    public bool HasDetail => Detail is not null;

    public async Task<TraxAnswer<ListPage<ConsoleRow>>> LoadAsync(
        TraxClient client,
        ListQuery query,
        CancellationToken cancellationToken
    )
    {
        var answer = await Load(client, query, cancellationToken);
        if (answer.Data is not { } page)
            return new() { Errors = answer.Errors, Failure = answer.Failure };
        return new()
        {
            Data = new ListPage<ConsoleRow>(
                page.Rows.Select(Row).ToList(),
                page.TotalCount,
                page.Estimated,
                page.NextCursor
            ),
        };
    }

    private ConsoleRow Row(TRow row) =>
        new(
            IdOf(row),
            Columns.Select(c => c.Value(row)).ToList(),
            Wire.Json(row),
            RunOf?.Invoke(row),
            CodeOf?.Invoke(row)
        );

    public Task<ActionOutcome> RunAsync(
        TraxClient client,
        string action,
        IReadOnlyList<long> ids,
        ActionOptions options,
        CancellationToken cancellationToken
    ) =>
        (
            Actions.FirstOrDefault(a => a.Name == action)
            ?? throw new InvalidOperationException(
                $"{Name} has no action '{action}'. It has {string.Join(", ", Actions.Select(a => a.Name))}."
            )
        ).Run(client, ids, options, cancellationToken);

    public Task<TraxAnswer<IReadOnlyList<(string Label, string Value)>>> DetailAsync(
        TraxClient client,
        long id,
        CancellationToken cancellationToken
    ) =>
        Detail is null
            ? Task.FromResult(
                new TraxAnswer<IReadOnlyList<(string Label, string Value)>>
                {
                    Failure = new TraxTransportFailure($"{Title} have no detail view."),
                }
            )
            : Detail(client, id, cancellationToken);
}
