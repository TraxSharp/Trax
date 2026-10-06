using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.RecordedDecision;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A run's recorded decisions as the operations surface reads them for the dashboard and the
/// GraphQL API: in the order they were recorded, paged by cursor, and with an answer to a question
/// about a <c>[TraxSensitive]</c> type, and every decision on a track taken on one, left out as
/// junction events leave them out.
///
/// <para>Enforces <c>Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[TestFixture]
public class OperationsServiceRecordedDecisionsTests : TestSetup
{
    /// <summary>A question whose answers must not be published.</summary>
    [TraxSensitive]
    private enum WithheldRefundTier
    {
        Small,
        Large,
    }

    private const string SensitiveKey = nameof(WithheldRefundTier);

    private IOperationsService _operations = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _operations = Scope.ServiceProvider.GetRequiredService<IOperationsService>();
    }

    [Test]
    public async Task Decisions_read_in_recorded_order_and_page_by_cursor()
    {
        var run = await SeedRun();
        var other = await SeedRun();
        for (var i = 0; i < 5; i++)
            await SeedDecision(run.Id, "Priority", i);
        await SeedDecision(other.Id, "Priority", 0);

        var first = await _operations.GetRecordedDecisionsAsync(
            run.Id,
            afterId: null,
            take: 3,
            CancellationToken.None
        );
        var second = await _operations.GetRecordedDecisionsAsync(
            run.Id,
            first.NextCursor,
            take: 3,
            CancellationToken.None
        );

        first.Items.Select(d => d.Occurrence).Should().Equal(0, 1, 2);
        second.Items.Select(d => d.Occurrence).Should().Equal(3, 4);
        first
            .Items.Concat(second.Items)
            .Should()
            .OnlyContain(d => d.MetadataId == run.Id && !d.AnswerWithheld && !d.TrackWithheld);
        first.Items[0].Answer.Should().Contain("High");
        first.Items[0].Question.Should().Contain("instructions");
    }

    [Test]
    public async Task A_run_with_no_decisions_reads_an_empty_page()
    {
        var page = await _operations.GetRecordedDecisionsAsync(
            999_999,
            afterId: null,
            take: 25,
            CancellationToken.None
        );

        page.Items.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
    }

    [TestCase(0, 1)]
    [TestCase(int.MaxValue, OperationsService.MaxPageSize)]
    public async Task The_page_size_is_clamped(int take, int expected)
    {
        var run = await SeedRun();
        await SeedDecision(run.Id, "Priority", 0);

        var page = await _operations.GetRecordedDecisionsAsync(
            run.Id,
            afterId: null,
            take,
            CancellationToken.None
        );

        page.Take.Should().Be(expected);
    }

    [Test]
    public async Task An_answer_to_a_sensitive_question_is_withheld_and_its_question_kept()
    {
        var run = await SeedRun();
        await SeedDecision(
            run.Id,
            SensitiveKey,
            0,
            refused: "Large is over the limit",
            shadows: """[{"answer":"Large"}]"""
        );
        await SeedDecision(run.Id, "Priority", 0);

        var page = await _operations.GetRecordedDecisionsAsync(
            run.Id,
            afterId: null,
            take: 25,
            CancellationToken.None
        );

        var withheld = page.Items[0];
        withheld.AnswerWithheld.Should().BeTrue();
        withheld.TrackWithheld.Should().BeFalse();
        withheld.QuestionKey.Should().Be(SensitiveKey);
        withheld.Question.Should().NotBeNull();
        withheld.Answer.Should().BeNull();
        withheld.Refused.Should().BeNull("a refusal's reason can quote the answer");
        withheld.IsRefused.Should().BeTrue();
        withheld.Shadows.Should().BeNull();
        page.Items[1].AnswerWithheld.Should().BeFalse("the question was not routed on");
        page.Items[1].Answer.Should().NotBeNull();
    }

    [Test]
    public async Task Every_decision_after_a_track_taken_on_a_withheld_answer_is_withheld()
    {
        var run = await SeedRun();
        await SeedDecision(run.Id, "Priority", 0);
        await SeedDecision(
            run.Id,
            SensitiveKey,
            0,
            routes: """[{"track":"Large","fallback_reason":null}]"""
        );
        await SeedDecision(run.Id, "Courier", 0);
        await SeedDecision(run.Id, "Courier", 1);

        var all = await _operations.GetRecordedDecisionsAsync(
            run.Id,
            afterId: null,
            take: 25,
            CancellationToken.None
        );
        var fromCursor = await _operations.GetRecordedDecisionsAsync(
            run.Id,
            all.Items[2].Id,
            take: 25,
            CancellationToken.None
        );

        all.Items[0].TrackWithheld.Should().BeFalse("it was decided before the track");
        all.Items[1].Routes.Should().BeNull("the track is the withheld answer");
        all.Items[1].QuestionKey.Should().Be(SensitiveKey);
        foreach (var later in all.Items.Skip(2).Concat(fromCursor.Items))
        {
            later.TrackWithheld.Should().BeTrue();
            later.QuestionKey.Should().BeNull("which question came next gives the track away");
            later.Question.Should().BeNull();
            later.Answer.Should().BeNull();
            later.Decider.Should().BeNull();
        }
        fromCursor
            .Items.Should()
            .ContainSingle("a page that starts after the track is withheld too");
    }

    private async Task<Metadata> SeedRun()
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        metadata.TrainState = TrainState.Completed;
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return metadata;
    }

    private async Task SeedDecision(
        long metadataId,
        string key,
        int occurrence,
        string? refused = null,
        string? shadows = null,
        string? routes = null
    )
    {
        DataContext.RecordedDecisions.Add(
            new RecordedDecision
            {
                MetadataId = metadataId,
                QuestionKey = key,
                Occurrence = occurrence,
                Fingerprint = new string('a', 64),
                Kind = "choice",
                Question =
                    $$"""{"type":"choice","key":"{{key}}","instructions":"Pick one","options":[]}""",
                Answer = """{"type":"choice","choice":"High","confidence":0.9}""",
                Refused = refused,
                Decider = "Tests.Decider",
                Shadows = shadows,
                Routes = routes,
                DecidedAt = DateTime.UtcNow,
            }
        );
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }
}
