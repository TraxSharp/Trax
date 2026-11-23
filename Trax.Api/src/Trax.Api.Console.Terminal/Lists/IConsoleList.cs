using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Lists;

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
