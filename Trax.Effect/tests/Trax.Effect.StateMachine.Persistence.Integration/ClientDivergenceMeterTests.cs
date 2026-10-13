using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// An advance whose client result differs from the server's is refused as <c>client-divergence</c>, writes nothing,
/// and is counted on the state-machine meter, so a twin drifting from its server shows up before users report it.
/// </summary>
[Property("adr", Adr)]
public class ClientDivergenceMeterTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Locked =
        """{"machine":"declarative-turnstile","version":1,"state":"Locked","context":{}}""";

    private static ISnapshotMachineRegistry NewRegistry()
    {
        var context = TestDb.NewContext();
        return new SnapshotMachineRegistry(
            new IMachine[] { new DeclarativeTurnstileMachine() },
            TestDb.NewStore(context),
            TestDb.NewClaims(context),
            new IdempotentEffect(TestDb.NewClaims(context)),
            new ServiceCollection().BuildServiceProvider()
        );
    }

    [Test]
    public async Task A_client_divergence_is_refused_and_metered()
    {
        var user = $"u-{Guid.NewGuid():N}";
        var principal = new FakePrincipal(user);
        var id = Guid.NewGuid();
        (
            await new SaveSnapshotJunction(NewRegistry(), principal).Run(
                new SaveSnapshotInput
                {
                    Machine = "declarative-turnstile",
                    Id = id,
                    Snapshot = Locked,
                }
            )
        ).Problem.Should().BeNull();

        var measurements = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (
                instrument.Meter.Name == StateMachineInstruments.MeterName
                && instrument.Name == StateMachineInstruments.ClientDivergence
            )
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>(
            (_, value, _, _) => Interlocked.Add(ref measurements, value)
        );
        listener.SetMeasurementEventCallback<int>(
            (_, value, _, _) => Interlocked.Add(ref measurements, value)
        );
        listener.Start();

        // The client claims the coin left the turnstile locked; the server unlocks it.
        var output = await new AdvanceSnapshotJunction(NewRegistry(), principal).Run(
            new AdvanceSnapshotInput
            {
                Machine = "declarative-turnstile",
                Id = id,
                Trigger = "Coin",
                Input = """{"coin":"quarter"}""",
                ClientResult = Locked,
            }
        );

        output.Problem!.Code.Should().Be("client-divergence");
        (await TestDb.NewStore().Get(user, id))!
            .Json.Should()
            .Contain("\"Locked\"", "a divergent advance persists nothing");
        listener.RecordObservableInstruments();
        Interlocked
            .Read(ref measurements)
            .Should()
            .Be(1, $"each divergence is counted once, so drift is visible. See {Adr}");
    }
}
