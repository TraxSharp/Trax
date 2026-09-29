using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

public enum LogState
{
    Open,
}

public enum LogTrigger
{
    Append,
}

/// <summary>A one-state machine whose only edge appends <c>input.text</c> to the text it already holds.</summary>
public sealed class LogMachine : Machine<LogState, LogTrigger>
{
    protected override void Configure(IMachineBuilder<LogState, LogTrigger> m)
    {
        m.Id("log").Version(1).StartsAt(LogState.Open, () => new JsonObject());
        m.In(LogState.Open)
            .On(LogTrigger.Append)
            .Reduce(
                (ctx, input) =>
                    new JsonObject
                    {
                        ["text"] =
                            (ctx["text"]?.GetValue<string>() ?? "")
                            + (input?["text"]?.GetValue<string>() ?? ""),
                    }
            )
            .To(LogState.Open);
    }
}

/// <summary>
/// What the authoritative advance refuses before it writes: an oversized input or result, and a result no
/// store can hold. In each case the stored draft must be exactly what it was.
/// </summary>
public class SnapshotAdvanceGuardTests
{
    private static readonly ISnapshotPrincipal User = new FakePrincipal("u");

    private static ISnapshotMachineRegistry NewRegistry()
    {
        var context = TestDb.NewContext();
        return new SnapshotMachineRegistry(
            new IMachine[] { new NoteMachine(), new LogMachine() },
            new EfSnapshotStore(context),
            new EfEffectClaimStore(context),
            new IdempotentEffect(new EfEffectClaimStore(context)),
            new ServiceCollection().BuildServiceProvider()
        );
    }

    private static async Task<Guid> Saved(string machine, string state, string context)
    {
        var id = Guid.NewGuid();
        (
            await new SaveSnapshotJunction(NewRegistry(), User).Run(
                new SaveSnapshotInput
                {
                    Machine = machine,
                    Id = id,
                    Snapshot =
                        $"{{\"machine\":\"{machine}\",\"version\":1,\"state\":\"{state}\",\"context\":{context}}}",
                }
            )
        ).Problem.Should().BeNull();
        return id;
    }

    private static Task<AdvanceSnapshotOutput> Advance(
        string machine,
        Guid id,
        string trigger,
        string? input,
        string? requestId = null
    ) =>
        new AdvanceSnapshotJunction(NewRegistry(), User).Run(
            new AdvanceSnapshotInput
            {
                Machine = machine,
                Id = id,
                Trigger = trigger,
                Input = input,
                RequestId = requestId,
            }
        );

    private static async Task<string> Load(string machine, Guid id) =>
        (
            await new LoadSnapshotJunction(NewRegistry(), User).Run(
                new LoadSnapshotInput { Machine = machine, Id = id }
            )
        ).Snapshot!;

    [Test]
    public async Task An_input_over_the_snapshot_limit_is_refused_before_it_is_parsed()
    {
        var id = await Saved("note", "Draft", "{}");

        // Not even valid JSON: the size is checked first, so this is too-large rather than malformed.
        var output = await Advance(
            "note",
            id,
            "Write",
            new string('{', SnapshotLimits.MaxSnapshotBytes + 1)
        );

        output.Problem!.Code.Should().Be("too-large");
    }

    [Test]
    public async Task An_advance_whose_result_exceeds_the_limit_is_refused_and_writes_nothing()
    {
        // Each half fits under the limit on its own; the appended result does not.
        var half = new string('x', SnapshotLimits.MaxSnapshotBytes / 2);
        var id = await Saved("log", "Open", new JsonObject { ["text"] = half }.ToJsonString());
        var before = await Load("log", id);

        var output = await Advance(
            "log",
            id,
            "Append",
            new JsonObject { ["text"] = half }.ToJsonString()
        );

        output.Problem!.Code.Should().Be("too-large");
        (await Load("log", id)).Should().Be(before);
    }

    [Test]
    public async Task An_advance_that_puts_a_NUL_in_the_context_is_refused_as_malformed_and_writes_nothing()
    {
        var id = await Saved("note", "Draft", "{}");

        var output = await Advance("note", id, "Write", "{\"text\":\"a\\u0000b\"}");

        output.Problem!.Code.Should().Be(RehydrationErrorCodes.Malformed);
        (await Load("note", id)).Should().Contain("\"context\":{}");
    }

    [Test]
    public async Task A_row_already_holding_a_number_outside_double_range_loads_as_a_problem()
    {
        // Stored before the engine refused such a value: the load must report it, not throw.
        var id = Guid.NewGuid();
        await TestDb
            .NewStore()
            .Upsert(
                "u",
                id,
                new Snapshot
                {
                    Machine = "note",
                    Version = 1,
                    State = "Draft",
                    Context = (JsonObject)JsonNode.Parse("{\"n\":1e400}")!,
                }
            );

        var load = await new LoadSnapshotJunction(NewRegistry(), User).Run(
            new LoadSnapshotInput { Machine = "note", Id = id }
        );

        load.Problem!.Code.Should().Be(RehydrationErrorCodes.Malformed);
    }
}
