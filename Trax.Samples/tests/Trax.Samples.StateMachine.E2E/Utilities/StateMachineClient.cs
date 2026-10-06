using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Samples.StateMachine.E2E.Utilities;

/// <summary>
/// The four generic <c>stateMachine</c> mutations, as the sample's web client sends them
/// (<c>web/src/traxTransport.ts</c>): the machine is an argument, the snapshot crosses the wire as a
/// JSON string, and a refusal comes back as <c>problem</c> in the data rather than as a GraphQL error.
/// </summary>
public class StateMachineClient(GraphQLClient graphQL)
{
    private const string Output = "output { snapshot problem { code message } }";

    public Task<GraphQLResponse> ListMachinesAsync(Caller caller) =>
        graphQL.SendAsync(
            "{ discover { stateMachine { listMachines { machines { name hasEffect } } } } }",
            caller
        );

    public Task<SnapshotResult> SaveAsync(
        Caller caller,
        string machine,
        Guid id,
        string snapshot
    ) =>
        Call(
            caller,
            "saveSnapshot",
            "SaveSnapshotInput",
            new
            {
                machine,
                id,
                snapshot,
            }
        );

    public Task<SnapshotResult> AdvanceAsync(
        Caller caller,
        string machine,
        Guid id,
        string trigger,
        string? input = null,
        string? requestId = null
    ) =>
        Call(
            caller,
            "advanceSnapshot",
            "AdvanceSnapshotInput",
            new
            {
                machine,
                id,
                trigger,
                input,
                requestId,
            }
        );

    public Task<SnapshotResult> LoadAsync(Caller caller, string machine, Guid id) =>
        Call(caller, "loadSnapshot", "LoadSnapshotInput", new { machine, id });

    public Task<SnapshotResult> SendAsync(
        Caller caller,
        string machine,
        Guid id,
        string? requestId = null
    ) =>
        Call(
            caller,
            "sendSnapshot",
            "SendSnapshotInput",
            new
            {
                machine,
                id,
                requestId,
            }
        );

    /// <summary>
    /// Sends one mutation and returns its raw response, for the tests that look at the GraphQL error
    /// rather than the mutation's output.
    /// </summary>
    public Task<GraphQLResponse> RawAsync(
        Caller caller,
        string field,
        string inputType,
        object input
    ) =>
        graphQL.SendAsync(
            $"mutation($i: {inputType}!) {{ dispatch {{ stateMachine {{ {field}(input: $i) {{ {Output} }} }} }} }}",
            caller,
            new { i = input }
        );

    private async Task<SnapshotResult> Call(
        Caller caller,
        string field,
        string inputType,
        object input
    )
    {
        var response = await RawAsync(caller, field, inputType, input);
        response
            .HasErrors.Should()
            .BeFalse($"{field} as {caller} should not error: {response.Raw}");

        var output = response.GetData("dispatch", "stateMachine", field, "output");
        var snapshot = output.GetProperty("snapshot");
        var problem = output.GetProperty("problem");

        return new SnapshotResult(
            snapshot.ValueKind == JsonValueKind.String ? snapshot.GetString() : null,
            problem.ValueKind == JsonValueKind.Object
                ? problem.GetProperty("code").GetString()
                : null,
            response.Raw
        );
    }
}

/// <summary>
/// A mutation's output: the snapshot as canonical JSON, or the code of the problem that refused it.
/// Exactly one of the two is set.
/// </summary>
public sealed record SnapshotResult(string? Wire, string? ProblemCode, string Raw)
{
    /// <summary>The snapshot, asserting that the mutation succeeded.</summary>
    public JsonObject Snapshot
    {
        get
        {
            ProblemCode.Should().BeNull($"the mutation should succeed: {Raw}");
            return JsonNode.Parse(Wire!)!.AsObject();
        }
    }

    public string State => Snapshot["state"]!.GetValue<string>();

    public JsonObject Context => Snapshot["context"]!.AsObject();
}
