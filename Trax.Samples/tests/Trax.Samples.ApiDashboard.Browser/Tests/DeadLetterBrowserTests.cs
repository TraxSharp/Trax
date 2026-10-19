using System.Text.RegularExpressions;
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
    [Covers("route:/runs/dead-letters")]
    [Covers("mutation:RequeueDeadLetter")]
    public async Task Requeue_sends_one_requeue_and_the_row_leaves_the_list()
    {
        var deadLetter = await Seed.DeadLetterAsync();
        var deadLetters = new DeadLettersPage(Console);

        await deadLetters.OpenAsync(Caller.Operator);
        await deadLetters.ShowAwaitingAsync();
        // Every test's dead letter failed the same way, so grouped it would sit in a closed group.
        await deadLetters.ShowAsListAsync();
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

    [Test]
    [Covers("route:/runs/dead-letters")]
    [Covers("mutation:AcknowledgeDeadLetter")]
    public async Task Acknowledge_closes_one_dead_letter_with_its_note()
    {
        var deadLetter = await Seed.DeadLetterAsync();
        var page = await OpenAwaitingListAsync();
        var row = page.Row(deadLetter.Id);
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = Render });

        var sent = Console.SendsAsync("AcknowledgeDeadLetter");
        await page.Button(row, "Acknowledge").ClickAsync();
        await Console.NoteAsync(
            $"Acknowledge dead letter #{deadLetter.Id}",
            "Supplier confirmed the feed was empty.",
            "Acknowledge"
        );
        await sent;

        await Assertions.Expect(row).ToHaveCountAsync(0, new() { Timeout = Render });
        var stored = await DeadLetter(deadLetter.Id);
        stored.Status.Should().Be(DeadLetterStatus.Acknowledged);
        stored.ResolutionNote.Should().Be("Supplier confirmed the feed was empty.");
        (await QueuedFor(deadLetter.Id)).Should().BeFalse("an acknowledgement queues no run");
    }

    [Test]
    [Covers("route:/runs/dead-letters")]
    [Covers("mutation:RequeueDeadLetters")]
    public async Task Requeue_selected_requeues_each_selected_dead_letter_in_one_request()
    {
        var (first, second) = await Seed.DeadLettersSharingAReasonAsync();
        var page = await OpenAwaitingListAsync();
        await SelectAsync(page, first.Id, second.Id);

        var sent = Console.SendsAsync("RequeueDeadLetters");
        await Console.Button("Requeue selected").ClickAsync();
        await Console.ConfirmAsync("Requeue dead letters", "Requeue 2 dead letter(s)?");
        var ids = (await sent).PostDataJSON()!.Value.GetProperty("variables").GetProperty("ids");
        ids.EnumerateArray()
            .Select(i => i.GetInt64())
            .Should()
            .BeEquivalentTo([first.Id, second.Id]);

        await Assertions
            .Expect(Console.Toast("2 dead letter(s) requeued."))
            .ToBeVisibleAsync(new() { Timeout = Render });
        foreach (var d in new[] { first, second })
        {
            (await DeadLetter(d.Id)).Status.Should().Be(DeadLetterStatus.Retried);
            (await QueuedFor(d.Id)).Should().BeTrue($"dead letter {d.Id} queued a run");
        }
        Checks.Sent.Count(s => s.OperationName == "RequeueDeadLetters").Should().Be(1);
    }

    [Test]
    [Covers("route:/runs/dead-letters")]
    [Covers("mutation:AcknowledgeDeadLetters")]
    public async Task Acknowledge_selected_closes_each_selected_dead_letter_with_the_note()
    {
        var (first, second) = await Seed.DeadLettersSharingAReasonAsync();
        var page = await OpenAwaitingListAsync();
        await SelectAsync(page, first.Id, second.Id);

        var sent = Console.SendsAsync("AcknowledgeDeadLetters");
        await Console.Button("Acknowledge selected").ClickAsync();
        await Console.NoteAsync(
            "Acknowledge 2 dead letter(s)",
            "Both feeds were retired.",
            "Acknowledge"
        );
        await sent;

        await Assertions
            .Expect(Console.Toast("2 dead letter(s) acknowledged."))
            .ToBeVisibleAsync(new() { Timeout = Render });
        foreach (var d in new[] { first, second })
        {
            var stored = await DeadLetter(d.Id);
            stored.Status.Should().Be(DeadLetterStatus.Acknowledged);
            stored.ResolutionNote.Should().Be("Both feeds were retired.");
        }
    }

    [Test]
    [Covers("route:/runs/dead-letters")]
    [Covers("mutation:RequeueAllDeadLetters")]
    public async Task Requeue_all_asks_for_all_typed_and_requeues_every_awaiting_dead_letter()
    {
        var deadLetter = await Seed.DeadLetterAsync();
        var page = new DeadLettersPage(Console);
        await page.OpenAsync(Caller.Operator);

        var sent = Console.SendsAsync("RequeueAllDeadLetters");
        await Console.Button("Requeue all").ClickAsync();
        await Console.ConfirmTypingAsync(
            "Requeue all dead letters",
            "Requeue every unresolved dead letter?",
            "all"
        );
        await sent;

        // The server requeues in the background; the page follows the job until it ends.
        await Assertions
            .Expect(Console.Toast("dead letter(s) requeued."))
            .ToBeVisibleAsync(new() { Timeout = BrowserTimeouts.Server.Ms() });
        (await DeadLetter(deadLetter.Id)).Status.Should().Be(DeadLetterStatus.Retried);
        (await QueuedFor(deadLetter.Id)).Should().BeTrue();
        (
            await Host.QueryAsync(dc =>
                dc.DeadLetters.AsNoTracking()
                    .CountAsync(d =>
                        d.Status == DeadLetterStatus.AwaitingIntervention && d.Id <= deadLetter.Id
                    )
            )
        )
            .Should()
            .Be(0, "every dead letter awaiting when it was asked is requeued");
    }

    [Test]
    [Covers("route:/runs/dead-letters")]
    [Covers("mutation:AcknowledgeAllDeadLetters")]
    public async Task Acknowledge_all_closes_every_awaiting_dead_letter_with_the_note()
    {
        var (first, second) = await Seed.DeadLettersSharingAReasonAsync();
        var page = new DeadLettersPage(Console);
        await page.OpenAsync(Caller.Operator);

        var sent = Console.SendsAsync("AcknowledgeAllDeadLetters");
        await Console.Button("Acknowledge all").ClickAsync();
        await Console.NoteAsync(
            "Acknowledge every unresolved dead letter",
            "The supplier was replaced.",
            "Acknowledge"
        );
        await sent;

        await Assertions
            .Expect(Console.Toast("dead letter(s) acknowledged."))
            .ToBeVisibleAsync(new() { Timeout = Render });
        foreach (var d in new[] { first, second })
            (await DeadLetter(d.Id)).ResolutionNote.Should().Be("The supplier was replaced.");
        (
            await Host.QueryAsync(dc =>
                dc.DeadLetters.AsNoTracking()
                    .CountAsync(d =>
                        d.Status == DeadLetterStatus.AwaitingIntervention && d.Id <= second.Id
                    )
            )
        )
            .Should()
            .Be(0);
    }

    [Test]
    [Covers("route:/runs/dead-letters")]
    [Covers("mutation:RequeueAllDeadLetters")]
    public async Task A_caller_the_operations_gate_refuses_cannot_requeue_all()
    {
        var deadLetter = await Seed.DeadLetterAsync();
        var page = new DeadLettersPage(Console);
        await page.OpenAsync(Caller.Refused);
        await Assertions.Expect(page.Refusal).ToBeVisibleAsync(new() { Timeout = Render });

        var sent = Console.SendsAsync("RequeueAllDeadLetters");
        await Console.Button("Requeue all").ClickAsync();
        await Console.ConfirmTypingAsync(
            "Requeue all dead letters",
            "Requeue every unresolved dead letter?",
            "all"
        );
        await sent;

        await Assertions
            .Expect(
                Page.GetByRole(AriaRole.Status)
                    .Filter(
                        new()
                        {
                            HasTextRegex = new Regex("not authori[sz]ed", RegexOptions.IgnoreCase),
                        }
                    )
            )
            .ToBeVisibleAsync(new() { Timeout = Render });
        (await DeadLetter(deadLetter.Id))
            .Status.Should()
            .Be(DeadLetterStatus.AwaitingIntervention, "the server refused the caller");
        (await QueuedFor(deadLetter.Id)).Should().BeFalse();
    }

    [Test]
    [Covers("route:/runs/dead-letters/:id")]
    [Covers("mutation:RequeueDeadLetter")]
    public async Task The_dead_letter_page_shows_why_and_requeues_it()
    {
        var deadLetter = await Seed.DeadLetterAsync();
        var page = new DeadLetterPage(Console, deadLetter.Id);
        await page.OpenAsync(Caller.Operator);
        await Assertions
            .Expect(page.Details)
            .ToContainTextAsync(deadLetter.Reason, new() { Timeout = Render });
        await Assertions.Expect(page.Details).ToContainTextAsync("Awaiting intervention");

        var sent = Console.SendsAsync("RequeueDeadLetter");
        await Console.Button("Requeue").ClickAsync();
        await Console.ConfirmAsync("Requeue dead letter", $"Requeue dead letter #{deadLetter.Id}?");
        await sent;

        // The requeue opens the work queue entry it wrote, which names the dead letter it retries.
        await Assertions
            .Expect(Console.Toast("has been requeued"))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(Page)
            .ToHaveURLAsync(new Regex(@"/runs/queued/\d+$"), new() { Timeout = Render });
        await Assertions
            .Expect(
                Page.GetByRole(AriaRole.Link, new() { Name = $"#{deadLetter.Id}", Exact = true })
            )
            .ToBeVisibleAsync(new() { Timeout = Render });
        (await DeadLetter(deadLetter.Id)).Status.Should().Be(DeadLetterStatus.Retried);
        (await QueuedFor(deadLetter.Id)).Should().BeTrue();
    }

    [Test]
    [Covers("route:/runs/dead-letters/:id")]
    [Covers("mutation:AcknowledgeDeadLetter")]
    public async Task The_dead_letter_page_acknowledges_it_with_a_note()
    {
        var deadLetter = await Seed.DeadLetterAsync();
        var page = new DeadLetterPage(Console, deadLetter.Id);
        await page.OpenAsync(Caller.Operator);

        var sent = Console.SendsAsync("AcknowledgeDeadLetter");
        await Console.Button("Acknowledge").ClickAsync(new() { Timeout = Render });
        await Console.NoteAsync(
            $"Acknowledge dead letter #{deadLetter.Id}",
            "Duplicate of an earlier import.",
            "Acknowledge"
        );
        await sent;

        await Assertions
            .Expect(page.Details)
            .ToContainTextAsync("Duplicate of an earlier import.", new() { Timeout = Render });
        await Assertions.Expect(Console.Button("Requeue")).ToHaveCountAsync(0);
        (await DeadLetter(deadLetter.Id)).Status.Should().Be(DeadLetterStatus.Acknowledged);
    }

    private static readonly float Render = BrowserTimeouts.Render.Ms();

    // The awaiting dead letters, one row each: every test's dead letter failed the same way, so grouped it
    // would sit in a closed group.
    private async Task<DeadLettersPage> OpenAwaitingListAsync()
    {
        var page = new DeadLettersPage(Console);
        await page.OpenAsync(Caller.Operator);
        await page.ShowAwaitingAsync();
        await page.ShowAsListAsync();
        return page;
    }

    private static async Task SelectAsync(DeadLettersPage page, params long[] ids)
    {
        foreach (var id in ids)
            await page.Row(id).GetByRole(AriaRole.Checkbox).CheckAsync(new() { Timeout = Render });
    }

    private static Task<Trax.Effect.Models.DeadLetter.DeadLetter> DeadLetter(long id) =>
        Host.QueryAsync(dc => dc.DeadLetters.AsNoTracking().SingleAsync(d => d.Id == id));

    private static Task<bool> QueuedFor(long deadLetter) =>
        Host.QueryAsync(dc =>
            dc.WorkQueues.AsNoTracking().AnyAsync(w => w.DeadLetterId == deadLetter)
        );
}
