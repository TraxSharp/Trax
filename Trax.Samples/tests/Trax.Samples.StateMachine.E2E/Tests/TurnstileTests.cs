using Trax.Samples.StateMachine.E2E.Fixtures;
using Trax.Samples.StateMachine.E2E.Utilities;

namespace Trax.Samples.StateMachine.E2E.Tests;

/// <summary>
/// The turnstile, the machine with no effect, driven over its typed operations: a draft is started or seeded, events advance
/// it on the server, and a trigger the machine does not allow is refused as data, leaving the stored
/// draft where it was.
/// </summary>
[TestFixture]
public class TurnstileTests : StateMachineTestFixture
{
    [Test]
    public async Task ListMachines_IsAnonymous_AndNamesBothMachines()
    {
        var response = await Machines.ListMachinesAsync(Anonymous);

        response.HasErrors.Should().BeFalse(response.Raw);
        var machines = response
            .GetData("discover", "stateMachine", "listMachines", "machines")
            .EnumerateArray()
            .Select(m =>
                (m.GetProperty("name").GetString(), m.GetProperty("hasEffect").GetBoolean())
            )
            .ToList();
        machines.Should().Equal((Checkout, true), (Turnstile, false));
    }

    [Test]
    public async Task ACoin_Unlocks_AndAPush_LocksAgain()
    {
        var id = Guid.NewGuid();
        await SeedAsync(Alice, id, TurnstileWire("Locked"));
        (await Machines.LoadAsync(Alice, Turnstile, id)).State.Should().Be("Locked");

        var unlocked = await Machines.AdvanceAsync(
            Alice,
            Turnstile,
            id,
            "Coin",
            input: """{"coin":"quarter"}"""
        );
        unlocked.State.Should().Be("Unlocked");
        unlocked.Context["paidWith"]!.GetValue<string>().Should().Be("quarter");

        var locked = await Machines.AdvanceAsync(Alice, Turnstile, id, "Push");
        locked.State.Should().Be("Locked");
        locked.Context.Count.Should().Be(0);

        (await Machines.LoadAsync(Alice, Turnstile, id)).State.Should().Be("Locked");
    }

    [Test]
    public async Task AStartedTurnstile_IsLocked_AndARetriedStartReturnsTheSameOne()
    {
        // The page starts its turnstile rather than saving one: the server mints the id.
        var requestId = $"start-{Guid.NewGuid():N}";

        var (id, started) = await Machines.StartAsync(Alice, Turnstile, requestId);
        var (retried, _) = await Machines.StartAsync(Alice, Turnstile, requestId);

        started.State.Should().Be("Locked");
        retried.Should().Be(id!.Value, "a retried start answers with the instance it created");
        (await Machines.AdvanceAsync(Alice, Turnstile, id.Value, "Coin", """{"coin":"dollar"}"""))
            .State.Should()
            .Be("Unlocked");
    }

    [Test]
    public async Task ACoinTheGuardRefuses_IsGuardFailed_AndTheDraftStaysLocked()
    {
        var id = Guid.NewGuid();
        await SeedAsync(Alice, id, TurnstileWire("Locked"));

        var penny = await Machines.AdvanceAsync(
            Alice,
            Turnstile,
            id,
            "Coin",
            input: """{"coin":"penny"}"""
        );

        penny.ProblemCode.Should().Be("GUARD_FAILED");
        penny.Wire.Should().BeNull();
        (await Machines.LoadAsync(Alice, Turnstile, id)).State.Should().Be("Locked");
    }

    [Test]
    public async Task APushWhileLocked_IsNoTransition()
    {
        var id = Guid.NewGuid();
        await SeedAsync(Alice, id, TurnstileWire("Locked"));

        (await Machines.AdvanceAsync(Alice, Turnstile, id, "Push"))
            .ProblemCode.Should()
            .Be("NO_TRANSITION");
        (await Machines.LoadAsync(Alice, Turnstile, id)).State.Should().Be("Locked");
    }

    [Test]
    public async Task ASnapshotAClientWrites_IsNotServed_AndNothingIsStored()
    {
        // Unlocked requires a non-empty paidWith, and a client cannot save its way past the coin: the host serves
        // the turnstile only through its events, with no save at all.
        var id = Guid.NewGuid();

        (await Machines.SaveAsync(Alice, Turnstile, id, TurnstileWire("Unlocked")))
            .ProblemCode.Should()
            .Be(StateMachineClient.NotServed);
        (await Machines.LoadAsync(Alice, Turnstile, id)).ProblemCode.Should().Be("NOT_FOUND");
    }

    [Test]
    public void ATurnstile_HasNoEffectToSend()
    {
        // The turnstile binds no effect, so its typed operations are its start, its read, its push, its discard and
        // its two events. Every test here sends these ids, so a manifest out of step with the host fails them all.
        StateMachineClient
            .Manifest.Machines.Single(m => m.Id == Turnstile)
            .Documents.Select(d => d.Operation)
            .Should()
            .BeEquivalentTo(["start", "get", "changed", "discard", "coin", "push"]);
    }

    [Test]
    public async Task AnUnknownMachine_IsNotServed()
    {
        (await Machines.LoadAsync(Alice, "vending", Guid.NewGuid()))
            .ProblemCode.Should()
            .Be(
                StateMachineClient.NotServed,
                "a machine the host does not expose has no operation"
            );
    }

    [Test]
    public async Task EachUserSeesOnlyTheirOwnDraft()
    {
        var id = Guid.NewGuid();
        await SeedAsync(Alice, id, TurnstileWire("Locked"));
        await Machines.AdvanceAsync(Alice, Turnstile, id, "Coin", input: """{"coin":"dollar"}""");

        (await Machines.LoadAsync(Bob, Turnstile, id)).ProblemCode.Should().Be("NOT_FOUND");

        // Bob's draft under the same id is his own, and leaves Alice's alone.
        await SeedAsync(Bob, id, TurnstileWire("Locked"));
        (await Machines.LoadAsync(Bob, Turnstile, id)).State.Should().Be("Locked");
        (await Machines.LoadAsync(Alice, Turnstile, id)).Context["paidWith"]!
            .GetValue<string>()
            .Should()
            .Be("dollar");
    }
}
