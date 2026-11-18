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
