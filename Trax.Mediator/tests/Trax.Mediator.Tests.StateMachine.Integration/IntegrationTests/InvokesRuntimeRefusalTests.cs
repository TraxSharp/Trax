using System.Reflection;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Tests.StateMachine.Integration.Fakes;
using Trax.Mediator.Tests.StateMachine.Integration.Fixtures;

namespace Trax.Mediator.Tests.StateMachine.Integration.IntegrationTests;

/// <summary>
/// What the startup check refuses is refused again at runtime, fail-closed, on a host whose hosted services never
/// started: a user-owned instance cannot queue a <c>[TraxBroadcast]</c> train, no instance can queue a train whose
/// output reaches a <c>[TraxSensitive]</c> member or apply such an output, and a host that replaced the mediator's
/// execution service is refused at startup rather than failing at the first entry. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class InvokesRuntimeRefusalTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md";

    private const string User = "u1";

    private InvokeHost _host = null!;

    [SetUp]
    public void SetUp() => _host = InvokeHost.Create(provider);

    [TearDown]
    public void TearDown() => _host.Dispose();

    [Test]
    public async Task A_user_owned_instance_cannot_queue_a_broadcast_train()
    {
        var id = Guid.NewGuid();

        var enter = () => _host.EnterRunning(User, id, "loud-stage");

        (await enter.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("'loud-stage'")
            .And.Contain("ILoudTrain")
            .And.Contain("[TraxBroadcast]", $"the startup check's refusal, made again. See {Adr}");
        (await _host.Row(id, "loud-stage"))!.State.Should().Be("Idle");
        (await _host.Runs(id)).Should().BeEmpty("nothing is queued");
    }

    [Test]
    public async Task No_instance_can_queue_a_train_whose_output_is_sensitive()
    {
        var id = Guid.NewGuid();

        var enter = () => _host.EnterRunning(User, id, "secret-stage");

        (await enter.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("ISecretTrain")
            .And.Contain("SecretOutput")
            .And.Contain("[TraxSensitive]", $"the startup check's refusal, made again. See {Adr}");
        (await _host.Row(id, "secret-stage"))!.State.Should().Be("Idle");
        (await _host.Runs(id)).Should().BeEmpty("nothing is queued");
    }

    [Test]
    public void A_sensitive_output_is_not_applied_to_the_context()
    {
        using var scope = _host.Scope();
        var machine = (IMachineInternals)
            scope.ServiceProvider.GetServices<IMachine>().OfType<SecretMachine>().Single();
        var running = new Snapshot
        {
            Machine = "secret-stage",
            Version = 1,
            State = "Running",
            Context = new JsonObject { ["source"] = "repo" },
        };

        var applied = machine.ApplyOutcome(
            running,
            new InvokeOutcome.Done(new JsonObject { ["artifact"] = "a", ["apiKey"] = "secret" })
        );

        applied
            .Should()
            .BeOfType<AdvanceResult.Rejected>(
                $"a sensitive output never reaches the stored context. See {Adr}"
            )
            .Which.Exception!.Message.Should()
            .Contain("[TraxSensitive]");

        machine
            .ApplyOutcome(running, new InvokeOutcome.Failed())
            .Should()
            .BeOfType<AdvanceResult.Transitioned>(
                "a failure carries no output, so it still applies"
            );
    }

    [Test]
    public void A_host_that_replaced_the_execution_service_is_refused_at_startup()
    {
        using var replaced = InvokeHost.Create(
            provider,
            new HostOptions
            {
                Configure = services =>
                    services.AddScoped(_ =>
                        DispatchProxy.Create<ITrainExecutionService, Unreachable>()
                    ),
            }
        );

        replaced
            .Problems<GoodMachine>()
            .Should()
            .ContainSingle($"the launcher queues through the mediator's own service. See {Adr}")
            .Which.Should()
            .Contain("'good-stage'")
            .And.Contain("IGoodTrain")
            .And.Contain("ITrainExecutionService");
    }

    /// <summary>An execution service that is not the mediator's, as a host's replacement would be.</summary>
    public class Unreachable : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException();
    }
}
