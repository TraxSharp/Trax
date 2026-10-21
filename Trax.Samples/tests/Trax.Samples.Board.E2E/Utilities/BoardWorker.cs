using System.Text.Json;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Board.E2E.Utilities;

/// <summary>
/// One worker on the board, with its own key, sending what an agent sends: <c>claimWork</c>, its assignment's typed
/// operations under <c>machines { assignment }</c>, and the board query. It holds one assignment per ticket.
/// </summary>
public sealed class BoardWorker(GraphQLClient graphQL, string apiKey)
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private const string Answer =
        "id revision state context { ticketId verdict failedCases } problem { code message }";

    private readonly Dictionary<string, Guid> _assignments = [];

    public Guid AssignmentOf(string ticketId) => _assignments[ticketId];

    /// <summary>Claims a ticket in <paramref name="lane"/>: the work package, or its problem.</summary>
    public async Task<JsonElement> ClaimAsync(string lane, string? ticketId)
    {
        var response = await graphQL.SendAsync(
            "mutation($i: ClaimWorkInput!) { dispatch { claimWork(input: $i) { output { "
                + "problem { code message } ticket { id state lane claimedBy } lane instructions tools events deadline } } } }",
            apiKey,
            new { i = new { lane, ticketId } }
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        return response.GetData("dispatch", "claimWork", "output");
    }

    /// <summary>Claims the ticket and starts an assignment on it; fails the test when the claim is refused.</summary>
    public async Task<Result> ClaimAndStartAsync(string lane, string ticketId)
    {
        var package = await ClaimAsync(lane, ticketId);
        package
            .GetProperty("problem")
            .ValueKind.Should()
            .Be(JsonValueKind.Null, $"{lane} should be able to claim {ticketId}: {package}");
        var started = await StartAsync(ticketId);
        started.Problem.Should().BeNull();
        return started;
    }

    public async Task<Result> StartAsync(string ticketId)
    {
        var result = await SendAsync(
            $"mutation($i: AssignmentStartInput!, $r: String!) {{ machines {{ assignment {{ start(input: $i, requestId: $r) {{ {Answer} }} }} }} }}",
            "start",
            new { i = new { ticketId }, r = $"start-{Guid.NewGuid():N}" }
        );
        if (result.Problem is null)
            _assignments[ticketId] = Guid.Parse(result.Answer.GetProperty("id").GetString()!);
        return result;
    }

    /// <summary>Sends one event with an input (<paramref name="inputType"/> names its GraphQL type).</summary>
    public Task<Result> SendAsync(
        string ticketId,
        string operation,
        string inputType,
        object input
    ) =>
        SendAsync(
            $"mutation($id: UUID!, $i: {inputType}!, $r: String!) {{ machines {{ assignment {{ "
                + $"{operation}(id: $id, input: $i, requestId: $r) {{ {Answer} }} }} }} }}",
            operation,
            new
            {
                id = _assignments[ticketId],
                i = input,
                r = $"{operation}-{Guid.NewGuid():N}",
            }
        );

    /// <summary>Sends one event that takes no input.</summary>
    public Task<Result> SendAsync(string ticketId, string operation) =>
        SendAsync(
            $"mutation($id: UUID!, $r: String!) {{ machines {{ assignment {{ "
                + $"{operation}(id: $id, requestId: $r) {{ {Answer} }} }} }} }}",
            operation,
            new { id = _assignments[ticketId], r = $"{operation}-{Guid.NewGuid():N}" }
        );

    public Task<Result> GetAsync(Guid id) =>
        SendAsync(
            $"query($id: UUID!) {{ machines {{ assignment {{ get(id: $id) {{ {Answer} }} }} }} }}",
            "get",
            new { id }
        );

    /// <summary>Sends an event and waits for the check it starts to answer: the assignment out of its checking state.</summary>
    public async Task<Result> SubmitAsync(
        string ticketId,
        string operation,
        string? inputType = null,
        object? input = null
    )
    {
        var sent = inputType is null
            ? await SendAsync(ticketId, operation)
            : await SendAsync(ticketId, operation, inputType, input!);
        sent.Problem.Should().BeNull($"{operation} should be accepted for checking");
        sent.State.Should().StartWith("Checking");
        Result? last = null;
        var answered = await Polling.WaitUntilAsync(
            async () =>
            {
                last = await GetAsync(_assignments[ticketId]);
                return last.State is not null
                    && !last.State.StartsWith("Checking", StringComparison.Ordinal);
            },
            Patience,
            TimeSpan.FromMilliseconds(100)
        );
        answered.Should().BeTrue($"the check should answer; the assignment is in {last?.State}");
        return last!;
    }

    public async Task<JsonElement> TicketAsync(string ticketId)
    {
        var response = await graphQL.SendAsync(
            "query { discover { board { board { tickets { id state lane claimedBy note by } } } } }",
            apiKey
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        return response
            .GetData("discover", "board", "board", "tickets")
            .EnumerateArray()
            .Single(t => t.GetProperty("id").GetString() == ticketId);
    }

    private async Task<Result> SendAsync(string query, string field, object variables)
    {
        var response = await graphQL.SendAsync(query, apiKey, variables);
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        var answer = response.GetData("machines", "assignment", field);
        return new Result(answer);
    }

    public sealed record Result(JsonElement Answer)
    {
        public string? State =>
            Answer.ValueKind == JsonValueKind.Object
            && Answer.TryGetProperty("state", out var s)
            && s.ValueKind == JsonValueKind.String
                ? s.GetString()
                : null;

        public string? Problem =>
            Answer.ValueKind == JsonValueKind.Object
            && Answer.TryGetProperty("problem", out var p)
            && p.ValueKind == JsonValueKind.Object
                ? p.GetProperty("code").GetString()
                : null;

        public string? Verdict =>
            Answer.TryGetProperty("context", out var c)
            && c.ValueKind == JsonValueKind.Object
            && c.GetProperty("verdict").ValueKind == JsonValueKind.String
                ? c.GetProperty("verdict").GetString()
                : null;
    }
}
