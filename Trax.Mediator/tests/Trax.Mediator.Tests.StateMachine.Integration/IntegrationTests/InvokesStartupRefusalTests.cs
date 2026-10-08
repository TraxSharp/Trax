using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Core.Train;
using Trax.Effect.Services.EffectJunction;
using Trax.Mediator.Tests.StateMachine.Integration.Fakes;
using Trax.Mediator.Tests.StateMachine.Integration.Fixtures;

namespace Trax.Mediator.Tests.StateMachine.Integration.IntegrationTests;

/// <summary>
/// A host refuses to start when a machine invokes a train it cannot queue in the advance's transaction, cancel from
/// another host, or authorize as declared, and each refusal names the machine, the state and the train, junction or
/// member at fault. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class InvokesStartupRefusalTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md";

    private InvokeHost _host = null!;

    [SetUp]
    public void SetUp() => _host = InvokeHost.Create(provider);

    [TearDown]
    public void TearDown() => _host.Dispose();

    [Test]
    public void A_train_the_host_can_run_cancel_and_authorize_is_not_refused()
    {
        _host.Problems<GoodMachine>().Should().BeEmpty();
        _host.Problems<PartitionMachine>().Should().BeEmpty();
    }

    [Test]
    public void A_plain_Junction_is_refused() =>
        _host
            .Problems<PlainJunctionMachine>()
            .Should()
            .ContainSingle(
                $"a plain junction never reads the cancel flag, so leaving the state could not stop it. See {Adr}"
            )
            .Which.Should()
            .Contain("'plain-junction-stage'")
            .And.Contain("Running")
            .And.Contain("IPlainJunctionTrain")
            .And.Contain("plain junction PlainStep at step 1");

    [Test]
    public void An_IChain_over_a_non_effect_interface_is_refused() =>
        _host
            .Problems<ContractMachine>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("IChain<IPlainContract>")
            .And.Contain("PlainContractStep is not an EffectJunction");

    [Test]
    public void A_train_that_is_not_a_ServiceTrain_is_refused() =>
        _host
            .Problems<BareMachine>()
            .Should()
            .Contain(p =>
                p.Contains("IBareTrain") && p.Contains("BareTrain is not a ServiceTrain")
            );

    [Test]
    public void A_recorder_refusal_is_refused() =>
        _host
            .Problems<RefusedChainMachine>()
            .Should()
            .Contain(p =>
                p.Contains("IRefusedChainTrain")
                && p.Contains("is refused")
                && p.Contains("IChain<RefusedStep> names a class")
            );

    [Test]
    public void A_plain_Junction_inside_a_Parallel_branch_is_refused() =>
        _host
            .Problems<ForkMachine>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("IForkTrain")
            .And.Contain("'plain'")
            .And.Contain("PlainForkRight");

    [Test]
    public void A_host_without_CancellationCheckProvider_is_refused()
    {
        using var host = InvokeHost.Create(provider, new HostOptions { JunctionProgress = false });

        host.Problems<GoodMachine>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'good-stage'")
            .And.Contain("cancel flag")
            .And.Contain("AddJunctionProgress()");
    }

    [Test]
    public void A_host_without_Mediator_is_refused()
    {
        using var host = InvokeHost.Create(provider, new HostOptions { Mediator = false });

        host.Problems<GoodMachine>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'good-stage'")
            .And.Contain("Trax.Mediator is not registered")
            .And.Contain("AddMediator");
    }

    [Test]
    public void Deferred_promotion_or_an_OnQueue_hook_is_refused()
    {
        var problems = _host.Problems<HookedMachine>();

        problems.Should().HaveCount(2);
        problems
            .Should()
            .Contain(p => p.Contains("IHookedTrain") && p.Contains("overrides OnQueue"));
        problems
            .Should()
            .Contain(p =>
                p.Contains("IHookedTrain") && p.Contains("overrides DeferQueuePromotion")
            );
    }

    [Test]
    public void Invokes_on_InMemory_is_refused()
    {
        using var host = InvokeHost.Create(StoreProvider.InMemory);

        host.Problems<GoodMachine>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'good-stage'")
            .And.Contain("InMemory")
            .And.Contain("transactions");
    }

    [Test]
    public void A_user_owned_machine_invoking_a_stricter_authorized_train_is_refused() =>
        _host
            .Problems<AdminMachine>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("IAdminTrain")
            .And.Contain("roles admin")
            .And.Contain("stricter than the machine's own mutations");

    [Test]
    public void A_user_owned_machine_invoking_a_broadcast_train_is_refused() =>
        _host
            .Problems<LoudMachine>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("ILoudTrain")
            .And.Contain("[TraxBroadcast]");

    [Test]
    public void A_system_owned_machine_invoking_a_train_with_user_only_requirements_is_refused() =>
        _host
            .Problems<SystemAdminMachine>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'system-admin-stage'")
            .And.Contain("IAdminTrain")
            .And.Contain("[TraxAuthorize]")
            .And.Contain("trusted execution scope");

    [Test]
    public void A_user_owned_machine_whose_outcome_enters_an_invoking_state_is_refused() =>
        _host
            .Problems<UserChainMachine>()
            .Should()
            .ContainSingle(
                "a run an outcome queues has no user present to authorize it, so only a system-owned "
                    + $"machine chains runs through outcomes. See {Adr}"
            )
            .Which.Should()
            .Contain("'user-chain-stage'")
            .And.Contain("user-owned")
            .And.Contain("OnDone outcome of Fetching")
            .And.Contain("enters Embedding")
            .And.Contain("Chain through an event the user sends")
            .And.Contain("SystemOwned()");

    [Test]
    public void A_system_owned_machine_or_a_user_event_may_chain_invoking_states()
    {
        _host
            .Problems<SystemChainMachine>()
            .Should()
            .BeEmpty("a system-owned machine's next run is authorized in the trusted scope");
        _host
            .Problems<UserContinueMachine>()
            .Should()
            .BeEmpty("a user's own event into an invoking state is authorized as that user");
    }

    [Test]
    public void An_output_reaching_a_sensitive_member_is_refused() =>
        _host
            .Problems<SecretMachine>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("ISecretTrain")
            .And.Contain("SecretOutput")
            .And.Contain("[TraxSensitive]");

    [Test]
    public void RailwayJunction_cannot_be_overridden()
    {
        var railway = typeof(EffectJunction<,>)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(m =>
                m.Name == nameof(EffectJunction<object, object>.RailwayJunction)
                && m.GetParameters()[1].ParameterType.IsGenericType
                && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Train<,>)
            );

        railway
            .IsFinal.Should()
            .BeTrue(
                $"a subclass that overrode it could run the junction without the cancel-flag check. See {Adr}"
            );
    }

    [Test]
    public async Task The_host_refuses_to_start_with_every_problem_listed()
    {
        var validator = _host
            .Services.GetServices<IHostedService>()
            .OfType<IHostedLifecycleService>()
            .Single(s => s.GetType().Name == "InvokesStartupValidator");

        var start = () => validator.StartingAsync(CancellationToken.None);

        var refused = await start.Should().ThrowAsync<InvalidOperationException>();
        refused
            .Which.Message.Should()
            .Contain("plain-junction-stage")
            .And.Contain("secret-stage")
            .And.Contain("user-chain-stage")
            .And.NotContain("system-chain-stage")
            .And.NotContain("good-stage");
    }
}
