using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary><see cref="Machine{TState,TTrigger}.CreateEffectRunner"/> over a draft service it did not build.</summary>
public class MachineEffectRunnerTests
{
    [Test]
    public void CreateEffectRunner_refuses_a_draft_service_the_machine_did_not_build_with_a_clear_message()
    {
        var machine = new OrderMachine();
        var services = new ServiceCollection()
            .AddSingleton<IOrderCharge>(new CountingEffect())
            .BuildServiceProvider();

        var act = () =>
            machine.CreateEffectRunner(
                new ForeignDraftService(),
                new IdempotentEffect(TestDb.NewClaims()),
                services
            );

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*ForeignDraftService*CreateService*")
            .Which.ParamName.Should()
            .Be("service");
    }

    [Test]
    public void CreateEffectRunner_refuses_a_null_draft_service_by_name()
    {
        var services = new ServiceCollection()
            .AddSingleton<IOrderCharge>(new CountingEffect())
            .BuildServiceProvider();

        var act = () =>
            new OrderMachine().CreateEffectRunner(
                null!,
                new IdempotentEffect(TestDb.NewClaims()),
                services
            );

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*was given null*")
            .Which.ParamName.Should()
            .Be("service");
    }

    private sealed class ForeignDraftService : ISnapshotDraftService
    {
        public Task<LoadResult> Load(
            string userKey,
            Guid id,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<AutosaveResult> Autosave(
            string userKey,
            Guid id,
            string snapshotJson,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<AdvanceOutcome> Advance(
            string userKey,
            Guid id,
            string trigger,
            JsonNode? input = null,
            string? requestId = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public string Serialize(Snapshot snapshot) => throw new NotSupportedException();
    }
}
