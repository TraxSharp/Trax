using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.Samples.StateMachine.E2E.Utilities;

/// <summary>
/// The sample's machines through their typed operations, as the sample's web client sends them
/// (<c>web/src/traxTransport.ts</c>): <c>mutation { machines { turnstile { start coin push discard } checkout { start
/// addItem next back pay reset discard } } }</c> and <c>query { machines { &lt;machine&gt; { get } } }</c>. A refusal
/// comes back as <c>problem</c> in the data rather than as a GraphQL error; a document the host does not serve (a
/// generic mutation, a field a machine does not have) is answered <see cref="NotServed"/>.
/// </summary>
public partial class StateMachineClient(GraphQLClient graphQL)
{
    /// <summary>The problem a request is answered with when the schema refuses its document.</summary>
    public const string NotServed = "not-served";

    // What each machine's answer selects of its context, and the version a snapshot of it carries.
    private static readonly Dictionary<string, (string Field, string Context, int Version)> Known =
        new(StringComparer.Ordinal)
        {
            ["turnstile"] = ("turnstile", "context { paidWith } ", 1),
            ["checkout"] = ("checkout", "context { items receipt total } ", 2),
        };

    /// <summary>
    /// Starts an instance of <paramref name="machine"/> from its declared start, as the page does: the server mints
    /// its id. Returns the id (null when refused) and the first snapshot.
    /// </summary>
    public async Task<(Guid? Id, SnapshotResult Result)> StartAsync(
        Caller caller,
        string machine,
        string? requestId = null
    )
    {
        var result = await MutateAsync(
            caller,
            machine,
            "start",
            $"requestId: {Literal(requestId ?? $"start-{Guid.NewGuid():N}")}"
        );
        return (result.Id, result);
    }

    /// <summary>Discards one of the caller's instances; true when it is gone.</summary>
    public async Task<bool> DiscardAsync(Caller caller, string machine, Guid id)
    {
        var response = await graphQL.SendAsync(
            $"mutation {{ machines {{ {Known[machine].Field} {{ discard(id: {Literal(id.ToString())}, "
                + $"requestId: {Literal($"discard-{Guid.NewGuid():N}")}) {{ discarded problem {{ code }} }} }} }} }}",
            caller
        );
        response
            .HasErrors.Should()
            .BeFalse($"discard as {caller} should not error: {response.Raw}");
        return response
            .GetData("machines", Known[machine].Field, "discard", "discarded")
            .GetBoolean();
    }

    public Task<GraphQLResponse> ListMachinesAsync(Caller caller) =>
        graphQL.SendAsync(
            "{ discover { stateMachine { listMachines { machines { name hasEffect } } } } }",
            caller
        );

    /// <summary>
    /// Tries the generic save, as a client that writes its own snapshot would. The sample serves no generic
    /// mutation, so the answer is <see cref="NotServed"/>.
    /// </summary>
    public async Task<SnapshotResult> SaveAsync(
        Caller caller,
        string machine,
        Guid id,
        string snapshot
    )
    {
        var response = await graphQL.SendAsync(
            "mutation($i: SaveSnapshotInput!) { dispatch { stateMachine { saveSnapshot(input: $i) { "
                + "output { snapshot problem { code } } } } } }",
            caller,
            new
            {
                i = new
                {
                    machine,
                    id,
                    snapshot,
                },
            }
        );
        return response.HasErrors
            ? new SnapshotResult(null, NotServed, response.Raw)
            : new SnapshotResult(
                null,
                response
                    .GetData("dispatch", "stateMachine", "saveSnapshot", "output", "problem")
                    .GetProperty("code")
                    .GetString(),
                response.Raw
            );
    }

    /// <summary>
    /// Sends <paramref name="trigger"/> to the instance as its own field, with <paramref name="input"/> (JSON) as
    /// its input. A field the machine does not have, or an argument it does not take, is <see cref="NotServed"/>.
    /// </summary>
    public Task<SnapshotResult> AdvanceAsync(
        Caller caller,
        string machine,
        Guid id,
        string trigger,
        string? input = null,
        string? requestId = null
    )
    {
        var args = $"id: {Literal(id.ToString())}";
        if (input is not null)
            args += $", input: {Literal(JsonNode.Parse(input))}";
        args += $", requestId: {Literal(requestId ?? $"req-{Guid.NewGuid():N}")}";
        return MutateAsync(caller, machine, JsonNamingPolicy.CamelCase.ConvertName(trigger), args);
    }

    /// <summary>The caller's instance, or the problem <c>not-found</c> when the caller has none under the id.</summary>
    public async Task<SnapshotResult> LoadAsync(Caller caller, string machine, Guid id)
    {
        if (!Known.TryGetValue(machine, out var known))
            return await NotAMachineAsync(caller, machine, id);

        var response = await graphQL.SendAsync(
            $"{{ machines {{ {known.Field} {{ get(id: {Literal(id.ToString())}) {{ id revision state "
                + $"{known.Context}problem {{ code message }} }} }} }} }}",
            caller
        );
        response.HasErrors.Should().BeFalse($"get as {caller} should not error: {response.Raw}");
        var read = response.GetData("machines", known.Field, "get");
        return read.ValueKind == JsonValueKind.Null
            ? new SnapshotResult(null, "not-found", response.Raw)
            : Result(machine, read, response.Raw);
    }

    /// <summary>
    /// Pays the checkout: its effect-bound <c>pay</c>, which takes no input, because the charge produces the
    /// receipt the edge records.
    /// </summary>
    public Task<SnapshotResult> SendAsync(
        Caller caller,
        string machine,
        Guid id,
        string? requestId = null
    ) =>
        MutateAsync(
            caller,
            machine,
            "pay",
            $"id: {Literal(id.ToString())}, requestId: {Literal(requestId ?? $"pay-{Guid.NewGuid():N}")}"
        );

    /// <summary>Sends <paramref name="query"/> as it is, for the tests about how the host answers a document.</summary>
    public Task<GraphQLResponse> RawAsync(Caller caller, string query) =>
        graphQL.SendAsync(query, caller);

    private async Task<SnapshotResult> MutateAsync(
        Caller caller,
        string machine,
        string field,
        string args
    )
    {
        if (!Known.TryGetValue(machine, out var known) || !GraphQLName().IsMatch(field))
            return new SnapshotResult(null, NotServed, "");

        var response = await graphQL.SendAsync(
            $"mutation {{ machines {{ {known.Field} {{ {field}({args}) {{ id revision state "
                + $"{known.Context}problem {{ code message }} }} }} }} }}",
            caller
        );
        return response.HasErrors
            ? new SnapshotResult(null, NotServed, response.Raw)
            : Result(machine, response.GetData("machines", known.Field, field), response.Raw);
    }

    // A machine the host does not serve has no field under machines: the schema refuses the read.
    private async Task<SnapshotResult> NotAMachineAsync(Caller caller, string machine, Guid id)
    {
        var response = await graphQL.SendAsync(
            $"{{ machines {{ {JsonNamingPolicy.CamelCase.ConvertName(machine)} {{ get(id: {Literal(id.ToString())}) {{ id }} }} }} }}",
            caller
        );
        response.HasErrors.Should().BeTrue($"no field serves '{machine}': {response.Raw}");
        return new SnapshotResult(null, NotServed, response.Raw);
    }

    // The typed answer as the snapshot the page holds: the context's null fields are the ones the state does not
    // hold, so they are left out.
    private static SnapshotResult Result(string machine, JsonElement answer, string raw)
    {
        if (answer.GetProperty("problem") is { ValueKind: JsonValueKind.Object } problem)
            return new SnapshotResult(null, problem.GetProperty("code").GetString(), raw);

        var context = new JsonObject();
        if (answer.TryGetProperty("context", out var fields))
            foreach (var field in fields.EnumerateObject())
                if (field.Value.ValueKind != JsonValueKind.Null)
                    context[field.Name] = JsonNode.Parse(field.Value.GetRawText());

        var wire = new JsonObject
        {
            ["machine"] = machine,
            ["version"] = Known[machine].Version,
            ["state"] = answer.GetProperty("state").GetString(),
            ["context"] = context,
        }.ToJsonString();
        return new SnapshotResult(wire, null, raw)
        {
            Id = answer.GetProperty("id") is { ValueKind: JsonValueKind.String } id
                ? Guid.Parse(id.GetString()!)
                : null,
        };
    }

    [GeneratedRegex("^[_A-Za-z][_0-9A-Za-z]*$")]
    private static partial Regex GraphQLName();

    // A value as a GraphQL literal: objects with bare keys, everything else as JSON writes it.
    private static string Literal(object? value) =>
        value switch
        {
            null => "null",
            JsonObject obj => "{ "
                + string.Join(", ", obj.Select(p => $"{p.Key}: {Literal(p.Value)}"))
                + " }",
            JsonArray array => "[" + string.Join(", ", array.Select(Literal)) + "]",
            JsonNode node => node.ToJsonString(),
            _ => JsonSerializer.Serialize(value),
        };
}

/// <summary>
/// An operation's answer: the snapshot as JSON, or the code of the problem that refused it. Exactly one of the two
/// is set.
/// </summary>
public sealed record SnapshotResult(string? Wire, string? ProblemCode, string Raw)
{
    /// <summary>The instance's id, when the answer carries one.</summary>
    public Guid? Id { get; init; }

    /// <summary>The snapshot, asserting that the operation succeeded.</summary>
    public JsonObject Snapshot
    {
        get
        {
            ProblemCode.Should().BeNull($"the operation should succeed: {Raw}");
            return JsonNode.Parse(Wire!)!.AsObject();
        }
    }

    public string State => Snapshot["state"]!.GetValue<string>();

    public JsonObject Context => Snapshot["context"]!.AsObject();
}
