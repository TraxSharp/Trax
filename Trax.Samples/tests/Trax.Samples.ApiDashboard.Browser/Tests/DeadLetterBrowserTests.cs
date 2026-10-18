using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Trax.Effect.Enums;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Pages;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// An operator resolves a dead letter from the console's dead-letter list, on a host that requires persisted
/// operations: the page sends the console's registered document by its id, and the host does what the operations
/// service does for every surface (central ADR 0022).
/// </summary>
[TestFixture]
public class DeadLetterBrowserTests : ConsoleTest
{
    [Test]
    public async Task Requeue_sends_one_requeue_and_the_row_leaves_the_list()
    {
        var deadLetter = await Seed.DeadLetterAsync();
        var deadLetters = new DeadLettersPage(Console);

        await deadLetters.OpenAsync(Caller.Operator);
        await deadLetters.ShowAwaitingAsync();
        var row = deadLetters.Row(deadLetter.Id);
        await Assertions
            .Expect(row)
            .ToBeVisibleAsync(new() { Timeout = BrowserTimeouts.Render.Ms() });

        var requeue = Page.WaitForRequestAsync(
            r =>
                r.Url.EndsWith("/trax/graphql")
                && r.PostData?.Contains("\"operationName\":\"RequeueDeadLetter\"") == true,
            new() { Timeout = BrowserTimeouts.Render.Ms() }
        );
        await deadLetters.RequeueAsync(deadLetter.Id);

        var sent = (await requeue).PostDataJSON()!.Value;
        sent.GetProperty("id")
            .GetString()
            .Should()
            .Be(
                ConsoleSetup.DocumentIds["RequeueDeadLetter"],
                "the page sends the console's registered requeue document by its id"
            );
        sent.TryGetProperty("query", out _).Should().BeFalse("no document text is sent");
        sent.GetProperty("variables").GetProperty("id").GetInt64().Should().Be(deadLetter.Id);

        await Assertions
            .Expect(row)
            .ToHaveCountAsync(0, new() { Timeout = BrowserTimeouts.Render.Ms() });

        var entry = await Host.QueryAsync(dc =>
            dc.WorkQueues.AsNoTracking().SingleOrDefaultAsync(w => w.DeadLetterId == deadLetter.Id)
        );
        entry.Should().NotBeNull("the requeue queues a run of the dead letter's manifest");
        entry!.ManifestId.Should().Be(deadLetter.ManifestId);
        (
            await Host.QueryAsync(dc =>
                dc.DeadLetters.AsNoTracking().SingleAsync(d => d.Id == deadLetter.Id)
            )
        )
            .Status.Should()
            .Be(DeadLetterStatus.Retried);

        Checks
            .Sent.Count(s => s.OperationName == "RequeueDeadLetter")
            .Should()
            .Be(1, "one click sends one requeue");
    }
}
