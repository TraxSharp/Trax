using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Lists;

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
