using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.Samples.Recovery.Machines;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Recovery.E2E.Utilities;

/// <summary>
/// One user's <c>topic-map</c> draft, driven through the machine's typed operations the way the page drives it:
/// <c>mutation { machines { topicMap { start chooseFields build ... discard } } }</c> and
/// <c>query { machines { topicMap { get } } }</c>. Start it from the machine's declared start, which mints its id on
/// the server, send it events, read it to come back. Nothing writes a snapshot: the host serves no save.
/// </summary>
/// <param name="graphQL">The client the operations go through.</param>
/// <param name="apiKey">The demo key the user acts with.</param>
/// <param name="id">The draft to come back to, or null for one <see cref="CreateAsync"/> starts.</param>
public sealed partial class TopicMapDraft(GraphQLClient graphQL, string apiKey, Guid? id = null)
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The problem a request is answered with when the host does not serve what it names: a document the schema
    /// refuses, such as a generic save or a field for a trigger no client sends.
    /// </summary>
    public const string NotServed = "not-served";

    private const string Field = "topicMap";

    private const string Answer =
        "id revision state context { fields fromYear toYear mapId papers topicPairs coCitationTrack } "
        + "problem { code message }";

    /// <summary>
    /// The draft's id: the one given to come back to a draft, or the one the server minted when
    /// <see cref="CreateAsync"/> started it. Until then, a fresh id no draft has.
    /// </summary>
    public Guid Id { get; private set; } = id ?? Guid.NewGuid();

    /// <summary>A snapshot of the machine as a client would write it.</summary>
    public static string Snapshot(string state, JsonObject? context = null) =>
        new JsonObject
        {
            ["machine"] = TopicMapMachine.MachineId,
            ["version"] = 1,
            ["state"] = state,
            ["context"] = context?.DeepClone() ?? new JsonObject(),
        }.ToJsonString();

    /// <summary>
    /// Starts the draft for the key's user, as the page does: the server mints its id and builds the wizard's
    /// first step from the machine's declared start.
    /// </summary>
    public async Task<Result> CreateAsync()
    {
        var result = await MutateAsync(
            "start",
            $"requestId: {Literal($"start-{Guid.NewGuid():N}")}"
        );
        result.Problem.Should().BeNull("the start should be accepted");
        Id = result.Id!.Value;
        return result;
    }

    /// <summary>
    /// Discards the draft, as the page's "Start a new map" does with one no build has begun; it resets any other
    /// with <see cref="TopicMapTrigger.StartOver"/>.
    /// </summary>
    public async Task<bool> DiscardAsync()
    {
        var response = await graphQL.SendAsync(
            $"mutation {{ machines {{ {Field} {{ discard(id: {Literal(Id.ToString())}, requestId: "
                + $"{Literal($"discard-{Guid.NewGuid():N}")}) {{ discarded problem {{ code }} }} }} }} }}",
            apiKey
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        return response.GetData("machines", Field, "discard", "discarded").GetBoolean();
    }

    /// <summary>
    /// Tries to save <paramref name="snapshot"/> with the generic save, as a client that writes its own snapshot
    /// would. The host does not serve it, so the answer is <see cref="NotServed"/>.
    /// </summary>
    public async Task<Result> SaveAsync(string snapshot)
    {
        var response = await graphQL.SendAsync(
            """
            mutation($i: SaveSnapshotInput!) {
              dispatch { stateMachine { saveSnapshot(input: $i) { output { snapshot problem { code } } } } }
            }
            """,
            apiKey,
            new
            {
                i = new
                {
                    machine = TopicMapMachine.MachineId,
                    id = Id.ToString(),
                    snapshot,
                },
            }
        );
        return response.HasErrors
            ? new Result(null, NotServed, null)
            : new Result(
                null,
                response
                    .GetData("dispatch", "stateMachine", "saveSnapshot", "output", "problem")
                    .GetProperty("code")
                    .GetString(),
                null
            );
    }

    /// <summary>
    /// Sends <paramref name="trigger"/> as its own field, with <paramref name="input"/> as the event's input. A
    /// trigger with no client field, such as an outcome or a timer, is answered <see cref="NotServed"/>.
    /// </summary>
    public async Task<Result> AdvanceAsync(string trigger, object? input = null)
    {
        var field = JsonNamingPolicy.CamelCase.ConvertName(trigger);
        if (!GraphQLName().IsMatch(field))
            return new Result(null, NotServed, null);

        var args = $"id: {Literal(Id.ToString())}";
        if (input is not null)
            args += $", input: {Literal(JsonSerializer.SerializeToNode(input, Camel))}";
        args += $", requestId: {Literal($"req-{Guid.NewGuid():N}")}";
        return await MutateAsync(field, args);
    }

    /// <summary>
    /// Reads the draft as its owner, as the page does when it comes back. A draft the caller does not have is
    /// answered with the problem <c>not-found</c>.
    /// </summary>
    public async Task<Result> LoadAsync()
    {
        var response = await graphQL.SendAsync(
            $"{{ machines {{ {Field} {{ get(id: {Literal(Id.ToString())}) {{ {Answer} }} }} }} }}",
            apiKey
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        var read = response.GetData("machines", Field, "get");
        return read.ValueKind == JsonValueKind.Null
            ? new Result(null, "not-found", null)
            : Parse(read);
    }

    /// <summary>Chooses every field of the corpus and the years, which enters <c>Building</c>.</summary>
    public async Task<Result> BuildAsync(IReadOnlyList<string> fields, int fromYear, int toYear)
    {
        (await AdvanceAsync(nameof(TopicMapTrigger.ChooseFields), new { fields }))
            .State.Should()
            .Be(nameof(TopicMapState.ChoosingRange));
        return await AdvanceAsync(nameof(TopicMapTrigger.Build), new { fromYear, toYear });
    }

    /// <summary>Loads the draft until it is in <paramref name="state"/>.</summary>
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
        reached.Should().BeTrue($"the draft should reach {state}; it is in {last?.State}");
        return last!;
    }

    private static readonly JsonSerializerOptions Camel = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^[_A-Za-z][_0-9A-Za-z]*$")]
    private static partial Regex GraphQLName();

    private async Task<Result> MutateAsync(string field, string args)
    {
        var response = await graphQL.SendAsync(
            $"mutation {{ machines {{ {Field} {{ {field}({args}) {{ {Answer} }} }} }} }}",
            apiKey
        );
        if (response.HasErrors)
            return new Result(null, NotServed, null);
        return Parse(response.GetData("machines", Field, field));
    }

    // The typed answer as the snapshot a client holds: the context's null fields are the ones the state does not
    // hold, so they are left out.
    private static Result Parse(JsonElement answer)
    {
        var id = answer.GetProperty("id") is { ValueKind: JsonValueKind.String } minted
            ? Guid.Parse(minted.GetString()!)
            : (Guid?)null;
        if (answer.GetProperty("problem") is { ValueKind: JsonValueKind.Object } problem)
            return new Result(null, problem.GetProperty("code").GetString(), id);

        var context = new JsonObject();
        foreach (var field in answer.GetProperty("context").EnumerateObject())
            if (field.Value.ValueKind != JsonValueKind.Null)
                context[field.Name] = JsonNode.Parse(field.Value.GetRawText());
        return new Result(
            JsonNode.Parse(Snapshot(answer.GetProperty("state").GetString()!, context))!.AsObject(),
            null,
            id
        );
    }

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

    /// <summary>An answer: the snapshot the server stored, or the code of the problem it was refused with.</summary>
    public sealed record Result(JsonObject? Snapshot, string? Problem, Guid? Id)
    {
        public string? State => Snapshot?["state"]?.GetValue<string>();

        public JsonObject Context => Snapshot!["context"]!.AsObject();
    }
}
