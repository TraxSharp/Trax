using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;

namespace Trax.Samples.StateMachine.E2E.Utilities;

/// <summary>
/// The sample's machines through their typed operations, as the sample's page sends them: each operation by the
/// persisted id its generated client holds (<c>web/src/trax/manifest.json</c>), with its values as variables, never a
/// document. A refusal comes back as <c>problem</c> in the data rather than as a GraphQL error. An operation the
/// generated client does not have (a generic save, an input an event does not take, a machine the host does not
/// expose) is answered <see cref="NotServed"/>: nothing could send it but a document, which the host refuses.
/// </summary>
public sealed partial class StateMachineClient(GraphQLClient graphQL)
{
    /// <summary>The problem a request is answered with when no generated operation serves it.</summary>
    public const string NotServed = "not-served";

    /// <summary>The client manifest the page is generated from, copied beside the tests.</summary>
    public static ClientManifest Manifest { get; } =
        ClientManifest.Read(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "manifest.json"))
        );

    // The version a snapshot of each machine carries, from its IR.
    private static int VersionOf(string machine) =>
        Manifest.Machines.Single(m => m.Id == machine).Ir["version"]!.GetValue<int>();

    /// <summary>The generated document of <paramref name="machine"/>'s <paramref name="operation"/>, or null.</summary>
    public static ClientManifestDocument? DocumentOf(string machine, string operation) =>
        Manifest
            .Machines.SingleOrDefault(m => m.Id == machine)
            ?.Documents.SingleOrDefault(d => d.Operation == operation);

    /// <summary>
    /// The id of the page's own document whose function is <paramref name="function"/>, as its generated module
    /// (<c>web/src/trax/documents/&lt;function&gt;.g.ts</c>) holds it.
    /// </summary>
    public static string DocumentIdOf(string function) =>
        DocumentId()
            .Match(
                File.ReadAllText(
                    Path.Combine(AppContext.BaseDirectory, "documents", $"{function}.g.ts")
                )
            )
            .Groups["id"]
            .Value;

    [System.Text.RegularExpressions.GeneratedRegex("DocumentId = \"(?<id>[^\"]+)\"")]
    private static partial System.Text.RegularExpressions.Regex DocumentId();

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
        var result = await OperateAsync(
            caller,
            machine,
            "start",
            new Dictionary<string, object?>
            {
                ["requestId"] = requestId ?? $"start-{Guid.NewGuid():N}",
            }
        );
        return (result.Id, result);
    }

    /// <summary>Discards one of the caller's instances; true when it is gone.</summary>
    public async Task<bool> DiscardAsync(Caller caller, string machine, Guid id)
    {
        var response = await graphQL.SendIdAsync(
            DocumentOf(machine, "discard")!.Id,
            caller,
            new { id, requestId = $"discard-{Guid.NewGuid():N}" }
        );
        response
            .HasErrors.Should()
            .BeFalse($"discard as {caller} should not error: {response.Raw}");
        return response.GetData("machines", machine, "discard", "discarded").GetBoolean();
    }

    public Task<GraphQLResponse> ListMachinesAsync(Caller caller) =>
        graphQL.SendIdAsync(DocumentIdOf("stateMachineListMachines"), caller, new { });

    /// <summary>
    /// Tries the generic save, as a client that writes its own snapshot would. The generated client has no such
    /// operation, and the host refuses the document a client would have to write for it.
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
        response
            .HasErrors.Should()
            .BeTrue($"the host serves no document sent as text: {response.Raw}");
        return new SnapshotResult(null, NotServed, response.Raw);
    }

    /// <summary>
    /// Sends <paramref name="trigger"/> to the instance through its generated operation, with <paramref name="input"/>
    /// (JSON) as its input. An event the machine does not have, or an input it does not take, is
    /// <see cref="NotServed"/>.
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
        var variables = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["requestId"] = requestId ?? $"req-{Guid.NewGuid():N}",
        };
        if (input is not null)
            variables["input"] = JsonNode.Parse(input);
        return OperateAsync(
            caller,
            machine,
            JsonNamingPolicy.CamelCase.ConvertName(trigger),
            variables
        );
    }

    /// <summary>The caller's instance, or the problem <c>NOT_FOUND</c> when the caller has none under the id.</summary>
    public async Task<SnapshotResult> LoadAsync(Caller caller, string machine, Guid id)
    {
        if (DocumentOf(machine, "get") is not { } get)
            return new SnapshotResult(null, NotServed, "");

        var response = await graphQL.SendIdAsync(get.Id, caller, new { id });
        response.HasErrors.Should().BeFalse($"get as {caller} should not error: {response.Raw}");
        var read = response.GetData("machines", machine, "get");
        return read.ValueKind == JsonValueKind.Null
            ? new SnapshotResult(null, "NOT_FOUND", response.Raw)
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
        OperateAsync(
            caller,
            machine,
            "pay",
            new Dictionary<string, object?>
            {
                ["id"] = id,
                ["requestId"] = requestId ?? $"pay-{Guid.NewGuid():N}",
            }
        );

    /// <summary>Sends <paramref name="query"/> as text, for the tests about how the host answers a document.</summary>
    public Task<GraphQLResponse> RawAsync(Caller caller, string query) =>
        graphQL.SendAsync(query, caller);

    private async Task<SnapshotResult> OperateAsync(
        Caller caller,
        string machine,
        string operation,
        Dictionary<string, object?> variables
    )
    {
        if (
            DocumentOf(machine, operation) is not { } document
            || variables.Keys.Any(v =>
                !document.Document.Contains($"${v}:", StringComparison.Ordinal)
            )
        )
            return new SnapshotResult(null, NotServed, "");

        var response = await graphQL.SendIdAsync(document.Id, caller, variables);
        response
            .HasErrors.Should()
            .BeFalse($"{machine}.{operation} as {caller} should not error: {response.Raw}");
        return Result(machine, response.GetData("machines", machine, operation), response.Raw);
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
            ["version"] = VersionOf(machine),
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
