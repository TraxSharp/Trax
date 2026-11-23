using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Lists;

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
        var answer = await Load(client, query, cancellationToken).ConfigureAwait(false);
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
            Actions.FirstOrDefault(a => string.Equals(a.Name, action, StringComparison.Ordinal))
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
