using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// The six generic <c>stateMachine</c> mutations are served only when the host opts in with
/// <c>ExposeGenericStateMachineMutations</c>, and then only behind the policy the opt-in names: without it the schema
/// has no <c>stateMachine</c> field, an opt-in without a policy refuses startup, and a host that exposes a machine
/// typed still serves its raw-delegate machines through the opt-in.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class GenericOptInE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_generic_opt_in";

    /// <summary>The policy the opt-in names: the Admin role.</summary>
    private const string ToolingPolicy = "StateMachineTooling";

    [OneTimeSetUp]
    public Task Up() => E2EHost.RecreateDatabaseAsync(Database);

    [OneTimeTearDown]
    public Task Down() => E2EHost.DropDatabaseAsync(Database);

    private static E2EHostOptions OptedIn() =>
        new()
        {
            Machines = o => o.ExposeGenericStateMachineMutations(g => g.Policy = ToolingPolicy),
            Services = s =>
                s.AddAuthorization(o => o.AddPolicy(ToolingPolicy, p => p.RequireRole("Admin"))),
        };

    [Test]
    public async Task Without_the_opt_in_the_schema_has_no_stateMachine_field()
    {
        using var host = await E2EHost.StartAsync(Database, new CountingCharge());
        var executor = await host.ExecutorAsync();

        var dispatch = SchemaWalk.FieldType(
            SchemaWalk.ObjectTypeNamed(executor, SchemaWalk.RootMutation),
            "dispatch"
        );

        (dispatch is null || !dispatch.Fields.Any(f => f.Name == "stateMachine"))
            .Should()
            .BeTrue(
                "the generic mutations are opt-in; a host that does not opt in serves none of them, per "
                    + Adr
            );
    }

    private static IEnumerable<TestCaseData> GenericMutations()
    {
        var id = Guid.NewGuid().ToString();
        yield return new TestCaseData(
            "saveSnapshot",
            "SaveSnapshotInput",
            new
            {
                machine = "turnstile",
                id,
                snapshot = TurnstileMachine.InitialJson,
            }
        ).SetArgDisplayNames("saveSnapshot");
        yield return new TestCaseData(
            "advanceSnapshot",
            "AdvanceSnapshotInput",
            new
            {
                machine = "turnstile",
                id,
                trigger = "Coin",
                input = "{\"coin\":\"quarter\"}",
            }
        ).SetArgDisplayNames("advanceSnapshot");
        yield return new TestCaseData(
            "loadSnapshot",
            "LoadSnapshotInput",
            new { machine = "turnstile", id }
        ).SetArgDisplayNames("loadSnapshot");
        yield return new TestCaseData(
            "sendSnapshot",
            "SendSnapshotInput",
            new
            {
                machine = "order",
                id,
                requestId = "send-1",
            }
        ).SetArgDisplayNames("sendSnapshot");
        yield return new TestCaseData(
            "startSnapshot",
            "StartSnapshotInput",
            new { machine = "turnstile", requestId = "start-1" }
        ).SetArgDisplayNames("startSnapshot");
        yield return new TestCaseData(
            "discardSnapshot",
            "DiscardSnapshotInput",
            new
            {
                machine = "turnstile",
                id,
                requestId = "discard-1",
            }
        ).SetArgDisplayNames("discardSnapshot");
    }

    [TestCaseSource(nameof(GenericMutations))]
    public async Task With_the_opt_in_the_gate_policy_is_enforced_on_all_six(
        string field,
        string inputType,
        object input
    )
    {
        using var host = await E2EHost.StartAsync(Database, new CountingCharge(), OptedIn());
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
    public async Task The_opt_in_without_a_policy_refuses_startup()
    {
        var start = async () =>
        {
            using var host = await E2EHost.StartAsync(
                Database,
                new CountingCharge(),
                new E2EHostOptions
                {
                    Machines = o => o.ExposeGenericStateMachineMutations(_ => { }),
                }
            );
        };

        (
            await start
                .Should()
                .ThrowAsync<InvalidOperationException>(
                    "the generic mutations fail closed: an opt-in names the policy that gates them, per "
                        + Adr
                )
        )
            .Which.ToString()
            .Should()
            .Contain(
                nameof(
                    Trax.Effect.StateMachine.Persistence.GenericStateMachineMutationOptions.Policy
                )
            );
    }

    [Test]
    public async Task A_host_with_unexposed_raw_delegate_machines_starts_and_serves_the_generic_opt_in()
    {
        using var host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            OptedIn() with
            {
                Served = [typeof(WizardMachine)],
            }
        );

        using var saved = await host.PostAsync(
            """
            mutation Save($input: SaveSnapshotInput!) {
              dispatch { stateMachine { saveSnapshot(input: $input) { output { snapshot problem { code } } } } }
            }
            """,
            new
            {
                input = new
                {
                    machine = "turnstile",
                    id = Guid.NewGuid().ToString(),
                    snapshot = TurnstileMachine.InitialJson,
                },
            },
            E2EHost.AdminApiKey
        );
        saved
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(
                "the raw-delegate turnstile stays usable through the generic opt-in: "
                    + saved.RootElement.GetRawText()
            );
        saved
            .RootElement.GetProperty("data")
            .GetProperty("dispatch")
            .GetProperty("stateMachine")
            .GetProperty("saveSnapshot")
            .GetProperty("output")
            .GetProperty("problem")
            .ValueKind.Should()
            .Be(JsonValueKind.Null);

        var executor = await host.ExecutorAsync();
        SchemaWalk
            .MachineType(executor, SchemaWalk.RootMutation, WizardMachine.MachineId)
            .Should()
            .NotBeNull(
                "the same host serves the exposed wizard typed beside the opt-in, per " + Adr
            );
    }
}
