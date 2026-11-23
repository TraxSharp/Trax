using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// The generic mutations are held to the machine's rules as its typed operations are: <c>loadSnapshot</c> to the
/// <c>get</c> rule, and every event to a request id that is not blank.
/// </summary>
[Property("adr", Adr)]
public class GenericRulesTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private static string NewUser() => $"user-{Guid.NewGuid():N}";

    private static ServiceProvider Caller(params string[] roles) =>
        new ServiceCollection()
            .AddSingleton<ICallerAuthorization>(new RolesCaller(roles))
            .BuildServiceProvider();

    private static ISnapshotMachineRegistry Registry(IMachine machine)
    {
        var context = TestDb.NewContext();
        return new SnapshotMachineRegistry(
            new[] { machine },
            TestDb.NewStore(context),
            TestDb.NewClaims(context),
            new IdempotentEffect(TestDb.NewClaims(context)),
            new ServiceCollection().BuildServiceProvider()
        );
    }

    private static async Task<Guid> Seed(string user, string machine)
    {
        var id = Guid.NewGuid();
        (
            await TestDb
                .NewStore()
                .Upsert(
                    user,
                    id,
                    new Snapshot
                    {
                        Machine = machine,
                        Version = 1,
                        State = nameof(TurnstileState.Locked),
                        Context = new JsonObject(),
                    }
                )
                .ConfigureAwait(false)
        ).Should().BeTrue();
        return id;
    }

    [Test]
    public async Task The_generic_load_applies_the_get_rule()
    {
        var owner = NewUser();
        var id = await Seed(owner, ViewerReadMachine.Id);
        var registry = Registry(new ViewerReadMachine());
        var withoutRole = Caller();
        await using var withoutRoleDisposal = withoutRole.ConfigureAwait(false);
        var withRole = Caller(ViewerReadMachine.Viewer);
        await using var withRoleDisposal = withRole.ConfigureAwait(false);

        var refused = await new LoadSnapshotJunction(
            registry,
            new FakePrincipal(owner),
            services: withoutRole
        ).Run(new LoadSnapshotInput { Machine = ViewerReadMachine.Id, Id = id });
        var read = await new LoadSnapshotJunction(
            registry,
            new FakePrincipal(owner),
            services: withRole
        ).Run(new LoadSnapshotInput { Machine = ViewerReadMachine.Id, Id = id });

        refused
            .Snapshot.Should()
            .BeNull($"the owner without the role the get rule names reads nothing. See {Adr}");
        refused
            .Problem.NotNull()
            .Code.Should()
            .Be(RefusalCode.NotFound, $"a refused read answers as an unknown id does. See {Adr}");
        read.Problem.Should().BeNull();
        read.Snapshot.Should().NotBeNull();
    }

    [TestCase("", TestName = "{m}(empty)")]
    [TestCase("   ", TestName = "{m}(whitespace)")]
    public async Task A_blank_request_id_is_refused_on_every_event_path(string requestId)
    {
        var owner = NewUser();
        var id = await Seed(owner, ViewerReadMachine.Id);
        var registry = Registry(new ViewerReadMachine());

        var generic = await new AdvanceSnapshotJunction(registry, new FakePrincipal(owner)).Run(
            new AdvanceSnapshotInput
            {
                Machine = ViewerReadMachine.Id,
                Id = id,
                Trigger = nameof(TurnstileTrigger.Coin),
                RequestId = requestId,
            }
        );
        var host = await registry
            .Service(ViewerReadMachine.Id)
            .NotNull()
            .Advance(owner, id, nameof(TurnstileTrigger.Coin), input: null, requestId);
        var client = await registry
            .Service(ViewerReadMachine.Id)
            .NotNull()
            .Advance(
                DeskRoles.Client(owner),
                id,
                nameof(TurnstileTrigger.Coin),
                input: null,
                requestId
            );

        generic
            .Problem.NotNull()
            .Code.Should()
            .Be(RefusalCode.InvalidRequestId, $"a blank id names no request. See {Adr}");
        foreach (var outcome in new[] { host, client })
            outcome
                .Should()
                .BeOfType<AdvanceOutcome.Rejected>()
                .Which.Reason.Should()
                .Be(RefusalCode.InvalidRequestId, $"a blank id names no request. See {Adr}");
        (await TestDb.NewStore().Get(owner, ViewerReadMachine.Id, id))
            .NotNull()
            .Json.Should()
            .Contain(nameof(TurnstileState.Locked), "nothing moved");
    }

    [TestCase("", TestName = "{m}(empty)")]
    [TestCase("   ", TestName = "{m}(whitespace)")]
    public async Task A_blank_request_id_on_a_send_is_refused_before_the_effect_runs(
        string requestId
    )
    {
        var owner = NewUser();
        var id = Guid.NewGuid();
        var effect = new CountingEffect();
        var registry = new SnapshotMachineRegistry(
            new IMachine[] { new OrderMachine() },
            TestDb.NewStore(),
            TestDb.NewClaims(),
            new IdempotentEffect(TestDb.NewClaims()),
            new ServiceCollection().AddSingleton<IOrderCharge>(effect).BuildServiceProvider()
        );
        await TestDb.Seed(owner, id, OrderMachine.ReviewSnapshot(1));

        var send = await new SendSnapshotJunction(registry, new FakePrincipal(owner)).Run(
            new SendSnapshotInput
            {
                Machine = "order",
                Id = id,
                RequestId = requestId,
            }
        );

        send.Problem.NotNull()
            .Code.Should()
            .Be(RefusalCode.InvalidRequestId, $"a blank id names no request. See {Adr}");
        effect.Calls.Should().Be(0, "a refused send delivers nothing");
    }

    private sealed class RolesCaller(string[] roles) : ICallerAuthorization
    {
        public bool IsInRole(string role) => roles.Contains(role, StringComparer.Ordinal);

        public Task<bool> SatisfiesPolicy(
            string policy,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(false);

        public Task<bool> IsPolicyRegistered(string policy) => Task.FromResult(true);
    }
}
