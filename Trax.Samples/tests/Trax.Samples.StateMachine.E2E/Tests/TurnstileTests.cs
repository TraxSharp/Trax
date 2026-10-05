using Trax.Samples.StateMachine.E2E.Fixtures;

namespace Trax.Samples.StateMachine.E2E.Tests;

/// <summary>
/// The turnstile, the machine with no effect, driven over GraphQL: a draft is saved, triggers advance
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
        (await Machines.SaveAsync(Alice, Turnstile, id, TurnstileWire("Locked")))
            .State.Should()
            .Be("Locked");

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
    public async Task ACoinTheGuardRefuses_IsGuardFailed_AndTheDraftStaysLocked()
    {
        var id = Guid.NewGuid();
        await Machines.SaveAsync(Alice, Turnstile, id, TurnstileWire("Locked"));

        var penny = await Machines.AdvanceAsync(
            Alice,
            Turnstile,
            id,
            "Coin",
            input: """{"coin":"penny"}"""
        );

        penny.ProblemCode.Should().Be("guard-failed");
        penny.Wire.Should().BeNull();
        (await Machines.LoadAsync(Alice, Turnstile, id)).State.Should().Be("Locked");
    }

    [Test]
    public async Task APushWhileLocked_IsNoTransition()
    {
        var id = Guid.NewGuid();
        await Machines.SaveAsync(Alice, Turnstile, id, TurnstileWire("Locked"));

        (await Machines.AdvanceAsync(Alice, Turnstile, id, "Push"))
            .ProblemCode.Should()
            .Be("no-transition");
        (await Machines.LoadAsync(Alice, Turnstile, id)).State.Should().Be("Locked");
    }

    [Test]
    public async Task ASnapshotWhoseContextBreaksItsState_IsRefused_AndNothingIsStored()
    {
        // Unlocked requires a non-empty paidWith; a client cannot save its way past the coin.
        var id = Guid.NewGuid();

        (await Machines.SaveAsync(Alice, Turnstile, id, TurnstileWire("Unlocked")))
            .ProblemCode.Should()
            .Be("invalid-context");
        (await Machines.LoadAsync(Alice, Turnstile, id)).ProblemCode.Should().Be("not-found");
    }

    [Test]
    public async Task SendingATurnstile_IsNoEffect()
    {
        var id = Guid.NewGuid();
        await Machines.SaveAsync(Alice, Turnstile, id, TurnstileWire("Locked"));

        (await Machines.SendAsync(Alice, Turnstile, id)).ProblemCode.Should().Be("no-effect");
    }

    [Test]
    public async Task AnUnknownMachine_IsRefused()
    {
        (await Machines.LoadAsync(Alice, "vending", Guid.NewGuid()))
            .ProblemCode.Should()
            .Be("unknown-machine");
    }

    [Test]
    public async Task EachUserSeesOnlyTheirOwnDraft()
    {
        var id = Guid.NewGuid();
        await Machines.SaveAsync(Alice, Turnstile, id, TurnstileWire("Locked"));
        await Machines.AdvanceAsync(Alice, Turnstile, id, "Coin", input: """{"coin":"dollar"}""");

        (await Machines.LoadAsync(Bob, Turnstile, id)).ProblemCode.Should().Be("not-found");

        // Bob's draft under the same id is his own, and leaves Alice's alone.
        await Machines.SaveAsync(Bob, Turnstile, id, TurnstileWire("Locked"));
        (await Machines.LoadAsync(Bob, Turnstile, id)).State.Should().Be("Locked");
        (await Machines.LoadAsync(Alice, Turnstile, id)).Context["paidWith"]!
            .GetValue<string>()
            .Should()
            .Be("dollar");
    }
}
