using Trax.Samples.Board.Auth;
using Trax.Samples.Board.E2E.Fixtures;

namespace Trax.Samples.Board.E2E.BoardTests;

/// <summary>
/// Nothing a client sends moves a ticket by itself: each refusal here is the server's, whatever the client claims.
/// </summary>
public class AuthorityTests : BoardTestFixture
{
    [Test]
    public async Task AWorker_CannotClaimFromAnotherLane()
    {
        var t = await FileAsync("boundary");
        var package = await Fix.ClaimAsync("triage", t);
        package.GetProperty("problem").GetProperty("code").GetString().Should().Be("FORBIDDEN");
        (await LaneOf(t)).Should().Be("triage");
    }

    [Test]
    public async Task ClaimingAgain_GivesTheWorkerItsOwnClaim_AndTheTicketStaysClaimedOnce()
    {
        var t = await FileAsync("boundary");
        await Triage.ClaimAndStartAsync("triage", t);
        var again = await new Utilities.BoardWorker(GraphQL, BoardKeys.TriageBot).ClaimAsync(
            "triage",
            t
        );
        // The same worker gets its own claim back; the ticket is still claimed once.
        again.GetProperty("problem").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        (await Triage.TicketAsync(t))
            .GetProperty("claimedBy")
            .GetString()
            .Should()
            .Be("triage-bot");
    }

    [Test]
    public async Task EvidenceOnATicketTheWorkerDidNotClaim_IsRejectedByTheCheck()
    {
        var t = await TriagedAsync("boundary");
        // repro-bot starts an assignment without claiming the ticket: the start is allowed, the evidence is not.
        (await Repro.StartAsync(t))
            .Problem.Should()
            .BeNull();
        var sent = await Repro.SubmitAsync(
            t,
            "submitRepro",
            "ReproCaseInput",
            new { subtotal = 50m, expectedShipping = 0m }
        );
        sent.State.Should().Be("Rejected");
        sent.Verdict.Should().Be($"{t} is not claimed by repro-bot; claim it first");
        (await StateOf(t)).Should().Be("Reproducing");
    }

    [Test]
    public async Task AnExpectedChargeThatIsNotThePolicys_IsRejected()
    {
        var t = await TriagedAsync("boundary");
        await Repro.ClaimAndStartAsync("reproduce", t);
        var sent = await Repro.SubmitAsync(
            t,
            "submitRepro",
            "ReproCaseInput",
            new { subtotal = 49.99m, expectedShipping = 0m }
        );
        sent.State.Should().Be("Rejected");
        sent.Verdict.Should().Be("expected $0.00 at $49.99, but the policy says $5.99");
        (await StateOf(t)).Should().Be("Reproducing");
    }

    [Test]
    public async Task AnotherWorkersAssignment_ReadsAsIfItDidNotExist()
    {
        var t = await FileAsync("boundary");
        await Triage.ClaimAndStartAsync("triage", t);
        // get answers null for another owner's id, exactly as for an unknown one.
        var read = await Repro.GetAsync(Triage.AssignmentOf(t));
        read.Answer.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        (await Triage.GetAsync(Triage.AssignmentOf(t))).State.Should().Be("Working");
    }

    [Test]
    public async Task TheTicketMachine_IsNotServedToClients()
    {
        var response = await GraphQL.SendAsync(
            "query { machines { ticket { __typename } } }",
            BoardKeys.Sam
        );
        response.HasErrors.Should().BeTrue("no client operation reaches a system-owned machine");
    }

    [Test]
    public async Task AnonymousCallers_AreRefused()
    {
        var response = await GraphQL.SendAsync(
            "mutation { dispatch { claimWork(input: { lane: \"triage\" }) { output { lane } } } }"
        );
        response.HasErrors.Should().BeTrue();
    }
}
