using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// <c>startSnapshot</c> and <c>discardSnapshot</c>, the generic opt-in's way to create and delete an instance: the
/// server mints the id from the machine's declared start, a retry of a start returns the instance it created, and a
/// discard deletes only the caller's own instance. Both sit behind the opt-in's policy like the other four.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class GenericStartDiscardE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_generic_start";

    /// <summary>The policy the gate test names: the Admin role.</summary>
    private const string ToolingPolicy = "StateMachineTooling";

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            E2EHostOptions.GenericOptIn()
        );
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host?.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
    }

    private static IEnumerable<TestCaseData> StartAndDiscard()
    {
        yield return new TestCaseData(
            "startSnapshot",
            "StartSnapshotInput",
            new { machine = "turnstile", requestId = "gate-start" }
        ).SetArgDisplayNames("startSnapshot");
        yield return new TestCaseData(
            "discardSnapshot",
            "DiscardSnapshotInput",
            new
            {
                machine = "turnstile",
                id = Guid.NewGuid().ToString(),
                requestId = "gate-discard",
            }
        ).SetArgDisplayNames("discardSnapshot");
    }

    [TestCaseSource(nameof(StartAndDiscard))]
    public async Task Start_and_discard_are_behind_the_opt_in_policy(
        string field,
        string inputType,
        object input
    )
    {
        using var host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Machines = o => o.ExposeGenericStateMachineMutations(g => g.Policy = ToolingPolicy),
                Services = s =>
                    s.AddAuthorization(o =>
                        o.AddPolicy(ToolingPolicy, p => p.RequireRole("Admin"))
                    ),
            }
        );
        var document = $$"""
            mutation M($input: {{inputType}}!) {
              dispatch { stateMachine { {{field}}(input: $input) { output { problem { code } } } } }
            }
            """;

        using var refused = await host.PostAsync(document, new { input }, E2EHost.BobKey);
        using var allowed = await host.PostAsync(document, new { input }, E2EHost.AdminApiKey);

        WizardOps
            .ErrorCodes(refused)
            .Should()
            .Contain(
                "TRAX_AUTHORIZATION",
                $"{field} is behind the opt-in's policy, which Bob does not satisfy, per {Adr}: "
                    + refused.RootElement.GetRawText()
            );
        WizardOps
            .ErrorCodes(allowed)
            .Should()
            .NotContain("TRAX_AUTHORIZATION", "an Admin satisfies the policy");
    }

    [Test]
    public async Task A_started_instance_moves_by_events_and_is_gone_once_discarded()
    {
        var requestId = $"start-{Guid.NewGuid():N}";

        var started = await Start(E2EHost.AliceKey, "turnstile", requestId);
        started.GetProperty("problem").ValueKind.Should().Be(JsonValueKind.Null);
        started.GetProperty("created").GetBoolean().Should().BeTrue();
        started.GetProperty("revision").GetInt64().Should().Be(1);
        var id = started.GetProperty("id").GetString()!;
        State(started).Should().Be("Locked");

        var retried = await Start(E2EHost.AliceKey, "turnstile", requestId);
        retried
            .GetProperty("id")
            .GetString()
            .Should()
            .Be(id, "a retried start returns the instance the first request created, per " + Adr);
        retried.GetProperty("created").GetBoolean().Should().BeFalse();

        var advanced = await Call(
            E2EHost.AliceKey,
            "advanceSnapshot",
            "AdvanceSnapshotInput",
            new
            {
                machine = "turnstile",
                id,
                trigger = "Coin",
                input = "{\"coin\":\"quarter\"}",
            },
            "snapshot problem { code }"
        );
        State(advanced).Should().Be("Unlocked");

        var discarded = await Discard(E2EHost.AliceKey, id, "discard-1");
        discarded.GetProperty("discarded").GetBoolean().Should().BeTrue();

        var loaded = await Load(E2EHost.AliceKey, id);
        loaded.GetProperty("problem").GetProperty("code").GetString().Should().Be("NOT_FOUND");

        (await Discard(E2EHost.AliceKey, id, "discard-2"))
            .GetProperty("discarded")
            .GetBoolean()
            .Should()
            .BeTrue("a discard is idempotent: an instance already gone answers as discarded");
    }

    [Test]
    public async Task Another_users_discard_answers_as_a_missing_one_and_deletes_nothing()
    {
        var started = await Start(E2EHost.AliceKey, "turnstile", $"start-{Guid.NewGuid():N}");
        var id = started.GetProperty("id").GetString()!;

        (await Discard(E2EHost.BobKey, id, "bob-discard"))
            .GetProperty("discarded")
            .GetBoolean()
            .Should()
            .BeTrue("someone else's id answers exactly as a missing one does");

        State(await Load(E2EHost.AliceKey, id))
            .Should()
            .Be("Locked", "Alice's instance is hers alone");
    }

    [Test]
    public async Task A_start_input_that_is_not_a_json_object_is_malformed()
    {
        var started = await Call(
            E2EHost.AliceKey,
            "startSnapshot",
            "StartSnapshotInput",
            new
            {
                machine = "turnstile",
                requestId = $"start-{Guid.NewGuid():N}",
                input = "[1,2]",
            },
            "id problem { code }"
        );

        started.GetProperty("problem").GetProperty("code").GetString().Should().Be("MALFORMED");
        started.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public async Task A_start_without_a_request_id_is_refused()
    {
        var started = await Call(
            E2EHost.AliceKey,
            "startSnapshot",
            "StartSnapshotInput",
            new { machine = "turnstile", requestId = " " },
            "id problem { code }"
        );

        started
            .GetProperty("problem")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("INVALID_REQUEST_ID", "a start is retried by its request id, per " + Adr);
    }

    private Task<JsonElement> Start(string key, string machine, string requestId) =>
        Call(
            key,
            "startSnapshot",
            "StartSnapshotInput",
            new { machine, requestId },
            "id created snapshot revision problem { code }"
        );

    private Task<JsonElement> Discard(string key, string id, string requestId) =>
        Call(
            key,
            "discardSnapshot",
            "DiscardSnapshotInput",
            new
            {
                machine = "turnstile",
                id,
                requestId,
            },
            "discarded problem { code }"
        );

    private Task<JsonElement> Load(string key, string id) =>
        Call(
            key,
            "loadSnapshot",
            "LoadSnapshotInput",
            new { machine = "turnstile", id },
            "snapshot problem { code }"
        );

    private async Task<JsonElement> Call(
        string key,
        string field,
        string inputType,
        object input,
        string selection
    )
    {
        using var response = await _host.PostAsync(
            $$"""
            mutation M($input: {{inputType}}!) {
              dispatch { stateMachine { {{field}}(input: $input) { output { {{selection}} } } } }
            }
            """,
            new { input },
            key
        );
        response
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(response.RootElement.GetRawText());
        return response
            .RootElement.GetProperty("data")
            .GetProperty("dispatch")
            .GetProperty("stateMachine")
            .GetProperty(field)
            .GetProperty("output")
            .Clone();
    }

    private static string State(JsonElement output) =>
        JsonDocument
            .Parse(output.GetProperty("snapshot").GetString()!)
            .RootElement.GetProperty("state")
            .GetString()!;
}
