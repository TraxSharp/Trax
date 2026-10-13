using System.Text.Json;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Recovery.E2E.Utilities;

/// <summary>
/// One user's food <c>order</c>, driven through the machine's typed operations as a client drives it:
/// <c>mutation { machines { order { start addItem placeOrder } } }</c> and <c>query { machines { order { get } } }</c>.
/// </summary>
/// <param name="graphQL">The client the operations go through.</param>
/// <param name="apiKey">The demo key the user acts with.</param>
public sealed class OrderDraft(GraphQLClient graphQL, string apiKey)
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private const string Answer =
        "id revision state dueAt context { items total } problem { code message }";

    /// <summary>The order's id, minted by the server when <see cref="StartAsync"/> started it.</summary>
    public Guid Id { get; private set; }

    /// <summary>Starts an order for the key's user, with an empty cart: the server mints its id.</summary>
    public async Task<Result> StartAsync()
    {
        var result = await SendAsync(
            $"mutation($r: String!) {{ machines {{ order {{ start(requestId: $r) {{ {Answer} }} }} }} }}",
            "start",
            new { r = $"start-{Guid.NewGuid():N}" }
        );
        result.Problem.Should().BeNull("the start should be accepted");
        Id = Guid.Parse(result.Answer.GetProperty("id").GetString()!);
        return result;
    }

    /// <summary>Puts <paramref name="item"/> in the cart.</summary>
    public Task<Result> AddItemAsync(string item) =>
        SendAsync(
            "mutation($id: UUID!, $i: NewItemInput!, $r: String!) { machines { order { "
                + $"addItem(id: $id, input: $i, requestId: $r) {{ {Answer} }} }} }} }}",
            "addItem",
            new
            {
                id = Id,
                i = new { item },
                r = $"add-{Guid.NewGuid():N}",
            }
        );

    /// <summary>Sends <c>placeOrder</c> under <paramref name="requestId"/>, or a fresh one.</summary>
    public Task<Result> PlaceOrderAsync(string? requestId = null) =>
        SendAsync(
            "mutation($id: UUID!, $r: String!) { machines { order { "
                + $"placeOrder(id: $id, requestId: $r) {{ {Answer} }} }} }} }}",
            "placeOrder",
            new { id = Id, r = requestId ?? $"place-{Guid.NewGuid():N}" }
        );

    /// <summary>Reads the order as its owner.</summary>
    public Task<Result> LoadAsync() =>
        SendAsync(
            $"query($id: UUID!) {{ machines {{ order {{ get(id: $id) {{ {Answer} }} }} }} }}",
            "get",
            new { id = Id }
        );

    /// <summary>Loads the order until it is in <paramref name="state"/>.</summary>
    public async Task<Result> WaitForStateAsync(string state)
    {
        Result? last = null;
        var reached = await Polling.WaitUntilAsync(
            async () =>
            {
                last = await LoadAsync();
                return last.State == state;
            },
            Patience,
            TimeSpan.FromMilliseconds(100)
        );
        reached.Should().BeTrue($"the order should reach {state}; it is in {last?.State}");
        return last!;
    }

    private async Task<Result> SendAsync(string query, string field, object variables)
    {
        var response = await graphQL.SendAsync(query, apiKey, variables);
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        return new Result(response.GetData("machines", "order", field).Clone());
    }

    /// <summary>An answer: the order as the server stored it, or the problem it was refused with.</summary>
    public sealed record Result(JsonElement Answer)
    {
        public string? Problem =>
            Answer.GetProperty("problem") is { ValueKind: JsonValueKind.Object } problem
                ? problem.GetProperty("code").GetString()
                : null;

        public string? State =>
            Answer.GetProperty("state") is { ValueKind: JsonValueKind.String } state
                ? state.GetString()
                : null;

        public long? Revision =>
            Answer.GetProperty("revision") is { ValueKind: JsonValueKind.Number } revision
                ? revision.GetInt64()
                : null;

        public JsonElement Context => Answer.GetProperty("context");
    }
}
