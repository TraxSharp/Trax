using Trax.Samples.Board.E2E.Fixtures;

namespace Trax.Samples.Board.E2E.BoardTests;

/// <summary>
/// The five scenarios the agents' scripts run (Trax.Samples.Board.Agents/src/scenarios.ts), sent as the agents send
/// them, with every refusal and every check the server ran asserted along the way.
/// </summary>
public class ScenarioTests : BoardTestFixture
{
    [Test]
    public async Task ATicket_GoesThroughEveryLane()
    {
        var t = await TriagedAsync("boundary");
        (await StateOf(t)).Should().Be("Reproducing");

        await Repro.ClaimAndStartAsync("reproduce", t);
        var at = await Repro.SubmitAsync(
            t,
            "submitRepro",
            "ReproCaseInput",
            new { subtotal = 50m, expectedShipping = 0m }
        );
        at.State.Should().Be("Accepted");
        at.Verdict.Should().Be("$50.00 pays $5.99; the policy says $0.00: reproduced");
        (await StateOf(t)).Should().Be("ReadyToFix");

        await Fix.ClaimAndStartAsync("fix", t);
        var fixedIt = await Fix.SubmitAsync(
            t,
            "submitFix",
            "RulesPatchInput",
            new { freeFrom = 50m, inclusive = true }
        );
        fixedIt.State.Should().Be("Accepted");
        fixedIt
            .Verdict.Should()
            .Be("free from $50.00: the repro case and 4 cases from earlier fixes pass");
        (await StateOf(t)).Should().Be("InQA");

        await Qa.ClaimAndStartAsync("qa", t);
        var swept = await Qa.SubmitAsync(
            t,
            "submitChecks",
            "QaCasesInput",
            new { subtotals = new[] { 49.99m, 50m, 50.01m } }
        );
        swept.Verdict.Should().Contain("3 of 3 pass");
        (await StateOf(t)).Should().Be("InReview");

        await Sam.ClaimAndStartAsync("review", t);
        (await Sam.SubmitAsync(t, "approve")).State.Should().Be("Accepted");
        var done = await Sam.TicketAsync(t);
        done.GetProperty("state").GetString().Should().Be("Done");
        done.GetProperty("note").GetString().Should().Be("approved by sam");
    }

    [Test]
    public async Task AnAgentThatSkipsAhead_IsRefusedTheClaimAndTheSelfApproval()
    {
        var t = await TriagedAsync("forty");

        // fix-bot tries to skip ahead: nothing in Fix to claim.
        var early = await Fix.ClaimAsync("fix", t);
        early
            .GetProperty("problem")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("NOTHING_TO_CLAIM");

        await Repro.ClaimAndStartAsync("reproduce", t);
        (
            await Repro.SubmitAsync(
                t,
                "submitRepro",
                "ReproCaseInput",
                new { subtotal = 40m, expectedShipping = 0m }
            )
        )
            .State.Should()
            .Be("Accepted");

        await Fix.ClaimAndStartAsync("fix", t);
        (
            await Fix.SubmitAsync(
                t,
                "submitFix",
                "RulesPatchInput",
                new { freeFrom = 40m, inclusive = true }
            )
        )
            .State.Should()
            .Be("Accepted");

        await Qa.ClaimAndStartAsync("qa", t);
        (
            await Qa.SubmitAsync(
                t,
                "submitChecks",
                "QaCasesInput",
                new { subtotals = new[] { 39.99m, 40m, 40.01m } }
            )
        )
            .Verdict.Should()
            .Contain("3 of 3 pass");
        (await StateOf(t)).Should().Be("InReview");

        // fix-bot starts an assignment of its own and tries to approve: the event's caller rule refuses it.
        (await Fix.StartAsync(t))
            .Problem.Should()
            .BeNull();
        (await Fix.SendAsync(t, "approve")).Problem.Should().Be("FORBIDDEN");
        (await StateOf(t)).Should().Be("InReview");

        await Sam.ClaimAndStartAsync("review", t);
        (await Sam.SubmitAsync(t, "approve")).State.Should().Be("Accepted");
        (await StateOf(t)).Should().Be("Done");
    }

    [Test]
    public async Task AFixThatBreaksAnEarlierCase_IsRefused_AndTheSecondPatchPasses()
    {
        var t = await TriagedAsync("threshold");
        await Repro.ClaimAndStartAsync("reproduce", t);
        (
            await Repro.SubmitAsync(
                t,
                "submitRepro",
                "ReproCaseInput",
                new { subtotal = 60m, expectedShipping = 0m }
            )
        )
            .State.Should()
            .Be("Accepted");

        await Fix.ClaimAndStartAsync("fix", t);
        var typo = await Fix.SubmitAsync(
            t,
            "submitFix",
            "RulesPatchInput",
            new { freeFrom = 6m, inclusive = true }
        );
        typo.State.Should().Be("Rejected");
        typo.Verdict.Should().Be("$20.00 pays $0.00 with this patch; the policy says $4.99");
        (await StateOf(t)).Should().Be("ReadyToFix");

        var right = await Fix.SubmitAsync(
            t,
            "submitFix",
            "RulesPatchInput",
            new { freeFrom = 60m, inclusive = true }
        );
        right.State.Should().Be("Accepted");
        (await StateOf(t)).Should().Be("InQA");
    }

    [Test]
    public async Task QaFindsTheCaseATooLooseFixLetThrough_AndSendsItBackToFix()
    {
        var t = await TriagedAsync("hundred");
        await Repro.ClaimAndStartAsync("reproduce", t);
        await Repro.SubmitAsync(
            t,
            "submitRepro",
            "ReproCaseInput",
            new { subtotal = 100m, expectedShipping = 0m }
        );

        await Fix.ClaimAndStartAsync("fix", t);
        (
            await Fix.SubmitAsync(
                t,
                "submitFix",
                "RulesPatchInput",
                new { freeFrom = 99m, inclusive = false }
            )
        )
            .State.Should()
            .Be("Accepted");

        await Qa.ClaimAndStartAsync("qa", t);
        var sweep = await Qa.SubmitAsync(
            t,
            "submitChecks",
            "QaCasesInput",
            new { subtotals = new[] { 99.5m, 100m, 100.5m } }
        );
        // QA did its job, so its work is accepted; the ticket is what goes back.
        sweep.State.Should().Be("Accepted");
        sweep.Verdict.Should().StartWith("$99.50 pays $0.00 on the patched rules");
        (await StateOf(t)).Should().Be("ReadyToFix");

        // fix-bot's earlier assignment is spent; it claims the ticket again and starts another.
        await Fix.ClaimAndStartAsync("fix", t);
        (
            await Fix.SubmitAsync(
                t,
                "submitFix",
                "RulesPatchInput",
                new { freeFrom = 100m, inclusive = true }
            )
        )
            .State.Should()
            .Be("Accepted");
        await Qa.ClaimAndStartAsync("qa", t);
        (
            await Qa.SubmitAsync(
                t,
                "submitChecks",
                "QaCasesInput",
                new { subtotals = new[] { 99.5m, 100m, 100.5m } }
            )
        )
            .Verdict.Should()
            .Contain("3 of 3 pass");
        (await StateOf(t)).Should().Be("InReview");
    }

    [Test]
    public async Task ATicketThatDoesNotReproduce_ClosesOnlyAfterTwoCasesTheServerRan()
    {
        var t = await TriagedAsync("fixed-already");
        await Repro.ClaimAndStartAsync("reproduce", t);

        var first = await Repro.SubmitAsync(
            t,
            "submitRepro",
            "ReproCaseInput",
            new { subtotal = 75m, expectedShipping = 0m }
        );
        first.State.Should().Be("Rejected");
        first.Verdict.Should().Be("$75.00 pays $0.00, which matches the policy: not reproduced");
        first.Answer.GetProperty("context").GetProperty("failedCases").GetInt32().Should().Be(1);

        (await Repro.SendAsync(t, "close")).Problem.Should().Be("GUARD_FAILED");

        await Repro.SubmitAsync(
            t,
            "submitRepro",
            "ReproCaseInput",
            new { subtotal = 50m, expectedShipping = 0m }
        );
        (await Repro.SubmitAsync(t, "close")).State.Should().Be("Closed");
        (await StateOf(t)).Should().Be("Closed");
    }
}
