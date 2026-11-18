using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Lists;

/// <summary>What a list is asked for: a page size, the cursor to start after, and the filters by name.</summary>
internal sealed record ListQuery(int Take, long? After, IReadOnlyDictionary<string, string> Filters)
{
    public static ListQuery First(int take = 25) =>
        new(take, After: null, new Dictionary<string, string>(StringComparer.Ordinal));
}
