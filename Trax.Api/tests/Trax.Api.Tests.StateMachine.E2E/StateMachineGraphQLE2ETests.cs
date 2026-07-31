using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// The four generic <c>stateMachine</c> mutations, driven over real HTTP through the full Trax GraphQL
/// stack against a real Postgres. This is the acceptance proof for the mutation layer: the trains register
/// in a live schema, auth is enforced at the edge (anonymous fails at HTTP 200, not a crash), a save →
/// advance → load flow persists server-side, the irreversible effect fires exactly once, and rejections
/// come back as typed data rather than transport errors.
/// </summary>
[TestFixture]
[NonParallelizable]
public class StateMachineGraphQLE2ETests
{
    private const string Database = "trax_statemachine_e2e";
    private IHost _host = null!;
    private CountingCharge _charge = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _charge = new CountingCharge();
        _host = await E2EHost.StartAsync(Database, _charge);
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
    }

    private const string SaveMutation = """
        mutation Save($input: SaveSnapshotInput!) {
          dispatch { stateMachine { saveSnapshot(input: $input) {
            output { snapshot problem { code message } }
          } } }
        }
        """;

    private const string AdvanceMutation = """
        mutation Advance($input: AdvanceSnapshotInput!) {
          dispatch { stateMachine { advanceSnapshot(input: $input) {
            output { snapshot problem { code message } }
          } } }
        }
        """;

    private const string LoadMutation = """
        mutation Load($input: LoadSnapshotInput!) {
          dispatch { stateMachine { loadSnapshot(input: $input) {
            output { snapshot problem { code message } }
          } } }
        }
        """;

    private const string SendMutation = """
        mutation Send($input: SendSnapshotInput!) {
          dispatch { stateMachine { sendSnapshot(input: $input) {
            output { snapshot problem { code message } }
          } } }
        }
        """;

    [Test]
    public async Task Anonymous_request_is_an_auth_error_at_http_200_not_a_crash()
    {
        using var doc = await _host.PostAsync(
            SaveMutation,
            new
            {
                input = new
                {
                    machine = "turnstile",
                    id = Guid.NewGuid().ToString(),
                    snapshot = TurnstileMachine.InitialJson,
                },
            }
        // no API key → anonymous
        );

        doc.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.GetArrayLength().Should().BeGreaterThan(0);
        errors[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("TRAX_AUTHORIZATION");
    }

    [Test]
    public async Task Save_advance_and_load_flow_persists_server_side_over_http()
    {
        var id = Guid.NewGuid().ToString();

        using var save = await _host.PostAsync(
            SaveMutation,
            new
            {
                input = new
                {
                    machine = "turnstile",
                    id,
                    snapshot = TurnstileMachine.InitialJson,
                },
            },
            E2EHost.AdminApiKey
        );
        NoErrors(save);
        Snapshot(Output(save, "saveSnapshot")).Should().Contain("\"state\":\"Locked\"");

        using var advance = await _host.PostAsync(
            AdvanceMutation,
            new
            {
                input = new
                {
                    machine = "turnstile",
                    id,
                    trigger = "Coin",
                    input = "{\"coin\":\"quarter\"}",
                },
            },
            E2EHost.AdminApiKey
        );
        NoErrors(advance);
        Snapshot(Output(advance, "advanceSnapshot")).Should().Contain("\"state\":\"Unlocked\"");

        // Load on the same id resumes the server-persisted draft.
        using var load = await _host.PostAsync(
            LoadMutation,
            new { input = new { machine = "turnstile", id } },
            E2EHost.AdminApiKey
        );
        NoErrors(load);
        Snapshot(Output(load, "loadSnapshot")).Should().Contain("\"state\":\"Unlocked\"");
    }

    [Test]
    public async Task Send_runs_the_irreversible_effect_exactly_once_over_http()
    {
        var id = Guid.NewGuid().ToString();

        using var save = await _host.PostAsync(
            SaveMutation,
            new
            {
                input = new
                {
                    machine = "order",
                    id,
                    snapshot = OrderMachine.ReviewSnapshot(1, 2),
                },
            },
            E2EHost.AdminApiKey
        );
        NoErrors(save);

        var before = _charge.Calls;

        using var send = await _host.PostAsync(
            SendMutation,
            new
            {
                input = new
                {
                    machine = "order",
                    id,
                    requestId = "r1",
                },
            },
            E2EHost.AdminApiKey
        );
        NoErrors(send);
        Snapshot(Output(send, "sendSnapshot")).Should().Contain("\"state\":\"Placed\"");
        (_charge.Calls - before).Should().Be(1, "the charge fires exactly once");

        // A retry (already Placed) must not deliver again.
        using var retry = await _host.PostAsync(
            SendMutation,
            new
            {
                input = new
                {
                    machine = "order",
                    id,
                    requestId = "r2",
                },
            },
            E2EHost.AdminApiKey
        );
        Snapshot(Output(retry, "sendSnapshot")).Should().Contain("\"state\":\"Placed\"");
        (_charge.Calls - before).Should().Be(1, "a resend does not re-charge");
    }

    [Test]
    public async Task An_unknown_machine_comes_back_as_a_typed_problem_not_an_error()
    {
        using var doc = await _host.PostAsync(
            SaveMutation,
            new
            {
                input = new
                {
                    machine = "nope",
                    id = Guid.NewGuid().ToString(),
                    snapshot = TurnstileMachine.InitialJson,
                },
            },
            E2EHost.AdminApiKey
        );

        NoErrors(doc);
        ProblemCode(Output(doc, "saveSnapshot")).Should().Be("unknown-machine");
    }

    [Test]
    public async Task Send_to_a_machine_with_no_effect_is_a_typed_problem()
    {
        var id = Guid.NewGuid().ToString();
        await _host.PostAsync(
            SaveMutation,
            new
            {
                input = new
                {
                    machine = "turnstile",
                    id,
                    snapshot = TurnstileMachine.InitialJson,
                },
            },
            E2EHost.AdminApiKey
        );

        using var doc = await _host.PostAsync(
            SendMutation,
            new { input = new { machine = "turnstile", id } },
            E2EHost.AdminApiKey
        );

        NoErrors(doc);
        ProblemCode(Output(doc, "sendSnapshot")).Should().Be("no-effect");
    }

    // ── response helpers ─────────────────────────────────────────────────

    private static JsonElement Output(JsonDocument doc, string field) =>
        doc
            .RootElement.GetProperty("data")
            .GetProperty("dispatch")
            .GetProperty("stateMachine")
            .GetProperty(field)
            .GetProperty("output");

    private static string? Snapshot(JsonElement output) =>
        output.GetProperty("snapshot").ValueKind == JsonValueKind.Null
            ? null
            : output.GetProperty("snapshot").GetString();

    private static string? ProblemCode(JsonElement output)
    {
        var problem = output.GetProperty("problem");
        return problem.ValueKind == JsonValueKind.Null
            ? null
            : problem.GetProperty("code").GetString();
    }

    private static void NoErrors(JsonDocument doc) =>
        doc
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse("the operation should not produce transport-level GraphQL errors");
}
