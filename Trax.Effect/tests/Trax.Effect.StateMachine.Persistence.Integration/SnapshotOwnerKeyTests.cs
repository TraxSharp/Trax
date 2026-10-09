using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// An empty or whitespace user key names no owner. Were it accepted, every caller a host's principal maps to it
/// would share one owner and could read and advance each other's drafts, so the mutations treat it as
/// unauthenticated and the draft service refuses it outright. Against real Postgres.
/// </summary>
public class SnapshotOwnerKeyTests
{
    private static ISnapshotMachineRegistry NewRegistry(IOrderCharge effect)
    {
        var context = TestDb.NewContext();
        var provider = new ServiceCollection().AddSingleton(effect).BuildServiceProvider();
        return new SnapshotMachineRegistry(
            new IMachine[] { new TurnstileMachine(), new OrderMachine() },
            TestDb.NewStore(context),
            TestDb.NewClaims(context),
            new IdempotentEffect(TestDb.NewClaims(context)),
            provider
        );
    }

    private static readonly Snapshot LockedTurnstile = new()
    {
        Machine = "turnstile",
        Version = 1,
        State = "Locked",
        Context = new JsonObject(),
    };

    private static Snapshot ReviewOrder() =>
        new()
        {
            Machine = "order",
            Version = 1,
            State = "Review",
            Context = new JsonObject { ["items"] = new JsonArray(1, 2), ["receipt"] = null },
        };

    private static async Task<List<SnapshotDraft>> Rows(Guid id) =>
        await TestDb
            .NewContext()
            .SnapshotDrafts.AsNoTracking()
            .Where(x => x.Id == id)
            .ToListAsync();

    [TestCase("")]
    [TestCase("  ")]
    public async Task An_empty_or_whitespace_key_is_unauthenticated_for_every_mutation_and_writes_nothing(
        string key
    )
    {
        // Drafts already stored under the blank key, as another caller mapped to it would have left them.
        var turnstileId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        (await TestDb.NewStore().Insert(key, turnstileId, LockedTurnstile)).Should().BeTrue();
        (await TestDb.NewStore().Insert(key, orderId, ReviewOrder())).Should().BeTrue();
        var turnstileBefore = (await Rows(turnstileId)).Single();
        var orderBefore = (await Rows(orderId)).Single();

        var principal = new FakePrincipal(key);
        var effect = new CountingEffect();

        var newId = Guid.NewGuid();
        var save = await new SaveSnapshotJunction(NewRegistry(effect), principal).Run(
            new SaveSnapshotInput
            {
                Machine = "turnstile",
                Id = newId,
                Snapshot = TestTurnstile.InitialJson,
            }
        );
        var advance = await new AdvanceSnapshotJunction(NewRegistry(effect), principal).Run(
            new AdvanceSnapshotInput
            {
                Machine = "turnstile",
                Id = turnstileId,
                Trigger = "Coin",
                Input = "{\"coin\":\"quarter\"}",
            }
        );
        var load = await new LoadSnapshotJunction(NewRegistry(effect), principal).Run(
            new LoadSnapshotInput { Machine = "turnstile", Id = turnstileId }
        );
        var send = await new SendSnapshotJunction(NewRegistry(effect), principal).Run(
            new SendSnapshotInput
            {
                Machine = "order",
                Id = orderId,
                RequestId = "r1",
            }
        );

        save.Problem!.Code.Should().Be("unauthenticated");
        save.Snapshot.Should().BeNull();
        advance.Problem!.Code.Should().Be("unauthenticated");
        advance.Snapshot.Should().BeNull();
        load.Problem!.Code.Should().Be("unauthenticated");
        load.Snapshot.Should().BeNull("the draft stored under the blank key is not handed out");
        send.Problem!.Code.Should().Be("unauthenticated");
        send.Snapshot.Should().BeNull();

        (await Rows(newId)).Should().BeEmpty("the save wrote no draft");
        effect.Calls.Should().Be(0, "the send ran no effect");
        var turnstileAfter = (await Rows(turnstileId)).Single();
        turnstileAfter.State.Should().Be("Locked");
        turnstileAfter.UpdatedAt.Should().Be(turnstileBefore.UpdatedAt);
        var orderAfter = (await Rows(orderId)).Single();
        orderAfter.State.Should().Be("Review");
        orderAfter.UpdatedAt.Should().Be(orderBefore.UpdatedAt);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public async Task The_draft_service_and_effect_runner_refuse_a_key_that_names_no_owner(
        string? key
    )
    {
        var service = TestTurnstile.Service(TestDb.NewStore());
        var id = Guid.NewGuid();

        await FluentActions
            .Awaiting(() => service.Load(key!, id))
            .Should()
            .ThrowAsync<ArgumentException>();
        await FluentActions
            .Awaiting(() => service.Autosave(key!, id, TestTurnstile.InitialJson))
            .Should()
            .ThrowAsync<ArgumentException>();
        await FluentActions
            .Awaiting(() =>
                service.Advance(key!, id, "Coin", new JsonObject { ["coin"] = "quarter" })
            )
            .Should()
            .ThrowAsync<ArgumentException>();
        await FluentActions
            .Awaiting(() =>
                service.Advance(key!, id, "Coin", null, null, clientResult: "{}", default)
            )
            .Should()
            .ThrowAsync<ArgumentException>();

        var effect = new CountingEffect();
        await FluentActions
            .Awaiting(() => NewRegistry(effect).EffectRunner("order")!.Run(key!, id, "r1"))
            .Should()
            .ThrowAsync<ArgumentException>();
        effect.Calls.Should().Be(0);

        (await Rows(id)).Should().BeEmpty();
    }
}
