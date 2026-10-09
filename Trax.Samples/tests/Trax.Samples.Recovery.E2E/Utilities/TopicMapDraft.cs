using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Samples.Recovery.Machines;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Recovery.E2E.Utilities;

/// <summary>
/// One user's <c>topic-map</c> draft, driven through the four generic <c>stateMachine</c> mutations the
/// way the page's twin drives it: save the first step, advance by triggers, load to come back.
/// </summary>
public sealed class TopicMapDraft(GraphQLClient graphQL, string apiKey, Guid? id = null)
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>The draft's id: a new one, or the one given to come back to a draft.</summary>
    public Guid Id { get; } = id ?? Guid.NewGuid();

    /// <summary>A snapshot of the machine as a client writes it.</summary>
    public static string Snapshot(string state, JsonObject? context = null) =>
        new JsonObject
        {
            ["machine"] = TopicMapMachine.MachineId,
            ["version"] = 1,
            ["state"] = state,
            ["context"] = context?.DeepClone() ?? new JsonObject(),
        }.ToJsonString();

    /// <summary>The wizard's first step, saved.</summary>
    public async Task<Result> CreateAsync() =>
        await SaveAsync(Snapshot(nameof(TopicMapState.ChoosingFields)));

    public Task<Result> SaveAsync(string snapshot) =>
        CallAsync("saveSnapshot", "SaveSnapshotInput", new { snapshot });

    public Task<Result> AdvanceAsync(string trigger, object? input = null) =>
        CallAsync(
            "advanceSnapshot",
            "AdvanceSnapshotInput",
            new { trigger, input = input is null ? null : JsonSerializer.Serialize(input, Camel) }
        );

    public Task<Result> LoadAsync() => CallAsync("loadSnapshot", "LoadSnapshotInput", new { });

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

    private async Task<Result> CallAsync(string field, string inputType, object fields)
    {
        var input = JsonSerializer.SerializeToNode(fields, Camel)!.AsObject();
        input["machine"] = TopicMapMachine.MachineId;
        input["id"] = Id.ToString();
        foreach (var key in input.Where(p => p.Value is null).Select(p => p.Key).ToList())
            input.Remove(key);

        var response = await graphQL.SendAsync(
            $$"""
            mutation($i: {{inputType}}!) {
              dispatch { stateMachine { {{field}}(input: $i) {
                output { snapshot problem { code message } }
              } } }
            }
            """,
            apiKey,
            new { i = input }
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        var output = response.GetData("dispatch", "stateMachine", field, "output");
        var snapshot = output.GetProperty("snapshot") is { ValueKind: JsonValueKind.String } s
            ? JsonNode.Parse(s.GetString()!)!.AsObject()
            : null;
        var problem = output.GetProperty("problem") is { ValueKind: JsonValueKind.Object } p
            ? p.GetProperty("code").GetString()
            : null;
        return new Result(snapshot, problem);
    }

    public sealed record Result(JsonObject? Snapshot, string? Problem)
    {
        public string? State => Snapshot?["state"]?.GetValue<string>();

        public JsonObject Context => Snapshot!["context"]!.AsObject();
    }
}
