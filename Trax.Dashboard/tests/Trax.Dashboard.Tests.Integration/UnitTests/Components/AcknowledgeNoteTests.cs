using AwesomeAssertions;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// An acknowledgement note is at most 1,000 characters, the length the scheduler and the API
/// accept: the note fields take no more, and a batch acknowledge the scheduler refuses (it
/// acknowledges nothing) is a warning that keeps the note, the input and the selection for the
/// operator to correct.
/// </summary>
[TestFixture]
public class AcknowledgeNoteTests
{
    private const string Refusal =
        "The note is 1001 characters; it may be at most 1000 characters.";

    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private RecordingScheduler _scheduler = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _ctx.Services.AddDashboardPageServices(_data);
        var (scheduler, recorder) = RecordingScheduler.Create();
        recorder.Respond = (method, _) =>
            method == nameof(ITraxScheduler.AcknowledgeDeadLettersAsync)
                ? Task.FromResult(new BatchDeadLetterResult(0, Refusal))
                : throw new InvalidOperationException($"{method} was not expected.");
        _scheduler = recorder;
        _ctx.Services.AddSingleton(scheduler);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task The_batch_note_takes_at_most_1000_characters()
    {
        await SeedDeadLetterAsync("reason-a");
        var page = _ctx.RenderComponent<DeadLettersPage>();
        WaitForRow(page, "reason-a");

        await ClickButton(page, "Acknowledge All");

        page.Find("input.cs-acknowledge-note").GetAttribute("maxlength").Should().Be("1000");
    }

    [Test]
    public async Task The_note_on_a_dead_letters_page_takes_at_most_1000_characters()
    {
        var id = await SeedDeadLetterAsync("reason-a");
        var page = _ctx.RenderComponent<DeadLetterDetailPage>(p => p.Add(x => x.DeadLetterId, id));
        page.WaitForElement("button:contains('Acknowledge')", WaitTimeout);

        await page.FindAll("button")
            .First(b => b.TextContent.Trim().EndsWith("Acknowledge"))
            .ClickAsync(new());

        page.Find("input.cs-acknowledge-note").GetAttribute("maxlength").Should().Be("1000");
    }

    [Test]
    public async Task A_refused_batch_acknowledge_warns_and_keeps_the_note_and_the_selection()
    {
        await SeedDeadLetterAsync("reason-a");
        var page = _ctx.RenderComponent<DeadLettersPage>();
        WaitForRow(page, "reason-a");
        await ToggleRow(page, "reason-a");

        await ClickButton(page, "Acknowledge Selected");
        await page.Find("input.cs-acknowledge-note").ChangeAsync(new() { Value = "too long" });
        await ClickButton(page, "Confirm");

        page.WaitForAssertion(
            () =>
                _ctx
                    .Services.GetRequiredService<NotificationService>()
                    .Messages.Should()
                    .ContainSingle(m =>
                        m.Severity == NotificationSeverity.Warning && m.Detail == Refusal
                    ),
            WaitTimeout
        );
        page.Find("input.cs-acknowledge-note").GetAttribute("value").Should().Be("too long");
        ButtonText(page, "Acknowledge Selected (1)").Should().NotBeEmpty();
        _scheduler
            .CallsTo(nameof(ITraxScheduler.AcknowledgeDeadLettersAsync))
            .Should()
            .ContainSingle();
    }

    private async Task<long> SeedDeadLetterAsync(string reason)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var group = new ManifestGroup { Name = "dead" };
        await db.Track(group);
        await db.SaveChanges(default);
        var manifest = Manifest.Create(new CreateManifest { Name = typeof(IAckTrain) });
        manifest.ManifestGroupId = group.Id;
        await db.Track(manifest);
        await db.SaveChanges(default);

        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = await db.Manifests.SingleAsync(m => m.Id == manifest.Id),
                Reason = reason,
                RetryCount = 3,
            }
        );
        await db.Track(deadLetter);
        await db.SaveChanges(default);
        return deadLetter.Id;
    }

    public interface IAckTrain { }
}
