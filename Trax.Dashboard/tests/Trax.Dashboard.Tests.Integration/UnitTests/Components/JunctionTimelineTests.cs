using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Api.DTOs;
using Trax.Core.Exceptions;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Enums;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The run page's junction timeline: one row per step <c>AddJunctionEvents</c> recorded, read
/// through <c>JunctionRunQueries.ForRun</c> as the API reads it, with a bar placed against the
/// run's start. It shows only the step's own fields: a withheld answer reads "withheld", a failed
/// step shows its failure class and exception type, never a message, and question and answer text
/// is encoded rather than rendered as markup.
/// </summary>
[TestFixture]
public class JunctionTimelineTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _ctx.Services.AddDashboardPageServices(_data);
        _ctx.Services.AddSingleton(UnusedService<ITraxScheduler>.Create());
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void A_run_with_no_steps_names_AddJunctionEvents_rather_than_showing_an_empty_box()
    {
        var timeline = Render(Run(TrainState.Completed, ended: 10), []);

        timeline.FindAll(".cs-jt-row").Should().BeEmpty();
        timeline.Find(".cs-jt-hint").TextContent.Should().Contain("AddJunctionEvents()");
    }

    [Test]
    public void A_finished_junction_is_a_bar_placed_against_the_runs_start()
    {
        var timeline = Render(
            Run(TrainState.Completed, ended: 10),
            [Junction(0, "LoadOrder", JunctionRunState.Completed, from: 2, to: 6)]
        );

        var bar = timeline.Find(".cs-jt-bar");
        bar.ClassList.Should().Contain("cs-jt--completed");
        bar.GetAttribute("style").Should().Contain("left: 20%").And.Contain("width: 40%");
        timeline.Find(".cs-jt-name").TextContent.Should().Contain("LoadOrder");
        timeline.Find(".cs-jt-duration").TextContent.Should().Be("4.0s");
    }

    [Test]
    public void A_running_junction_extends_to_now()
    {
        var timeline = Render(
            Run(TrainState.InProgress, ended: null),
            [Junction(0, "Charge", JunctionRunState.InProgress, from: 5, to: null)],
            now: Start.AddSeconds(10)
        );

        var bar = timeline.Find(".cs-jt-bar");
        bar.ClassList.Should().Contain("cs-jt--in-progress");
        bar.GetAttribute("style").Should().Contain("left: 50%").And.Contain("width: 50%");
        timeline.Find(".cs-jt-duration").TextContent.Should().Be("5.0s so far");
    }

    [Test]
    public void A_step_with_no_recorded_end_on_a_finished_run_ends_at_the_runs_end()
    {
        // A host that dies mid-junction: the run is ended by the reaper, the step never is.
        var run = Run(TrainState.Failed, ended: 10);
        var steps = new[] { Junction(0, "Charge", JunctionRunState.InProgress, from: 2, to: null) }
            .Select(JunctionStep.From)
            .ToList();
        var timeline = _ctx.RenderComponent<JunctionTimeline>(p =>
            p.Add(x => x.Metadata, run)
                .Add(x => x.Steps, steps)
                .Add(x => x.Now, Start.AddMinutes(5))
        );

        timeline
            .Find(".cs-jt-bar")
            .GetAttribute("style")
            .Should()
            .Contain("left: 20%")
            .And.Contain("width: 80%", "a step cannot outlast its run");
        timeline
            .Find(".cs-jt-duration")
            .TextContent.Should()
            .Be("end not recorded", "the step's end was never written, so it is not running");

        var renders = timeline.RenderCount;
        timeline.SetParametersAndRender(p => p.Add(x => x.Now, Start.AddMinutes(30)));

        timeline
            .RenderCount.Should()
            .Be(renders, "nothing on a finished run's timeline moves with the clock");
        timeline.Find(".cs-jt-bar").GetAttribute("style").Should().Contain("width: 80%");
    }

    [Test]
    public void A_decision_shows_its_question_answer_confidence_and_replay()
    {
        var step = Decision(1, JunctionRunKind.Choice, "Acme.Routing.Refund");
        step.Answer = "Approve";
        step.Confidence = 0.82;
        step.Replayed = true;

        var timeline = Render(Run(TrainState.Completed, ended: 10), [step]);

        var detail = timeline.Find(".cs-jt-detail").TextContent;
        timeline.Find(".cs-jt-question").TextContent.Should().Be("Acme.Routing.Refund");
        timeline.Find(".cs-jt-answer").TextContent.Should().Be("Approve");
        detail.Should().Contain("82% confidence");
        timeline.FindAll(".cs-jt-replayed").Should().ContainSingle();
        timeline
            .FindAll(".cs-jt-point")
            .Should()
            .ContainSingle("a question is a moment, not a span");
    }

    [Test]
    public void A_withheld_answer_reads_withheld_and_nothing_else()
    {
        // The writer stores neither when the answer is withheld; the page must not show them even
        // if a row carried them.
        var step = Decision(1, JunctionRunKind.Score, "Acme.Sensitive.Salary");
        step.AnswerWithheld = true;
        step.Answer = "MARKER-ANSWER-91234";
        step.Confidence = 0.77;

        var timeline = Render(Run(TrainState.Completed, ended: 10), [step]);

        timeline.Find(".cs-jt-answer").TextContent.Should().Be("withheld");
        timeline.Markup.Should().NotContain("MARKER-ANSWER-91234").And.NotContain("77%");
    }

    [Test]
    public void A_withheld_step_reads_withheld_even_when_handed_an_answer()
    {
        // JunctionStep.From already drops a withheld answer; the component does not rely on it.
        var step = new JunctionStep(
            1,
            JunctionRunKind.Choice,
            "Acme.Sensitive.Plan",
            JunctionRunState.Completed,
            Start.AddSeconds(2),
            null,
            null,
            null,
            null,
            "Acme.Sensitive.Plan",
            "MARKER-ANSWER-7781",
            0.66,
            false,
            null,
            AnswerWithheld: true,
            null
        );

        var timeline = _ctx.RenderComponent<JunctionTimeline>(p =>
            p.Add(x => x.Metadata, Run(TrainState.Completed, ended: 10))
                .Add(x => x.Steps, new[] { step })
                .Add(x => x.Now, Start.AddMinutes(5))
        );

        timeline.Find(".cs-jt-answer").TextContent.Should().Be("withheld");
        timeline.Markup.Should().NotContain("MARKER-ANSWER-7781").And.NotContain("66%");
    }

    [Test]
    public void A_junction_whose_name_is_withheld_never_renders_its_stored_name()
    {
        // Effect stores "(withheld)"; even a row still holding the real name must not show it.
        var route = Decision(1, JunctionRunKind.Route, "Acme.Sensitive.Plan");
        route.AnswerWithheld = true;
        var hidden = Junction(2, "MARKER-JUNCTION-NAME-4471", JunctionRunState.Completed, 5, 6);
        hidden.NameWithheld = true;
        hidden.TrackPosition = 1;

        var timeline = Render(Run(TrainState.Completed, ended: 10), [route, hidden]);

        var row = timeline.Find(".cs-jt-row[data-position='2']");
        row.QuerySelector(".cs-jt-title")!.TextContent.Should().Be("withheld");
        row.QuerySelector(".cs-jt-title")!.ClassList.Should().Contain("cs-jt-answer--withheld");
        row.ClassList.Should().Contain("cs-jt-row--on-track");
        row.QuerySelector(".cs-jt-on-track")!.TextContent.Should().Be("on track of step #1");
        timeline.Markup.Should().NotContain("MARKER-JUNCTION-NAME-4471");
    }

    [Test]
    public void A_step_marked_name_withheld_hides_a_name_it_was_handed()
    {
        // JunctionStep.From already replaces a withheld name; the component does not rely on it.
        var step = JunctionStep.From(
            Junction(3, "MARKER-HANDED-NAME-9902", JunctionRunState.Completed, 1, 2)
        ) with
        {
            NameWithheld = true,
            TrackPosition = 0,
        };

        var timeline = _ctx.RenderComponent<JunctionTimeline>(p =>
            p.Add(x => x.Metadata, Run(TrainState.Completed, ended: 10))
                .Add(x => x.Steps, new[] { step })
                .Add(x => x.Now, Start.AddMinutes(5))
        );

        timeline.Find(".cs-jt-title").TextContent.Should().Be("withheld");
        timeline.Markup.Should().NotContain("MARKER-HANDED-NAME-9902");
    }

    [Test]
    public void A_question_on_a_withheld_track_shows_no_key_and_no_answer()
    {
        // As the API maps a question or route asked on a withheld track: its name is withheld,
        // its key, answer and confidence absent.
        var step = new JunctionStep(
            4,
            JunctionRunKind.Choice,
            JunctionEventPayload.WithheldName,
            JunctionRunState.Completed,
            Start.AddSeconds(4),
            null,
            null,
            null,
            null,
            QuestionKey: null,
            Answer: null,
            Confidence: null,
            Replayed: false,
            Decider: null,
            AnswerWithheld: false,
            Attempt: null,
            NameWithheld: true,
            TrackPosition: 1
        );

        var timeline = _ctx.RenderComponent<JunctionTimeline>(p =>
            p.Add(x => x.Metadata, Run(TrainState.Completed, ended: 10))
                .Add(x => x.Steps, new[] { step })
                .Add(x => x.Now, Start.AddMinutes(5))
        );

        var row = timeline.Find(".cs-jt-row[data-position='4']");
        row.QuerySelector(".cs-jt-title")!.TextContent.Should().Be("Choice");
        var question = row.QuerySelector(".cs-jt-question")!;
        question.TextContent.Should().Be("withheld");
        question.ClassList.Should().Contain("cs-jt-answer--withheld");
        question.HasAttribute("title").Should().BeFalse();
        row.QuerySelector(".cs-jt-answer")!.TextContent.Should().Be("withheld");
        row.QuerySelector(".cs-jt-confidence").Should().BeNull();
        row.QuerySelector(".cs-jt-on-track")!.TextContent.Should().Be("on track of step #1");
        row.TextContent.Should()
            .NotContain("no answer")
            .And.NotContain(JunctionEventPayload.WithheldName);
    }

    [Test]
    public void A_question_on_a_withheld_track_hides_a_key_and_answer_it_was_handed()
    {
        // The API already drops them; the component does not rely on it.
        var step = new JunctionStep(
            4,
            JunctionRunKind.Route,
            "MARKER-ROUTE-NAME-3310",
            JunctionRunState.Completed,
            Start.AddSeconds(4),
            null,
            null,
            null,
            null,
            QuestionKey: "MARKER-QUESTION-KEY-3311",
            Answer: "MARKER-ANSWER-3312",
            Confidence: 0.58,
            Replayed: false,
            Decider: null,
            AnswerWithheld: false,
            Attempt: null,
            NameWithheld: true,
            TrackPosition: 1
        );

        var timeline = _ctx.RenderComponent<JunctionTimeline>(p =>
            p.Add(x => x.Metadata, Run(TrainState.Completed, ended: 10))
                .Add(x => x.Steps, new[] { step })
                .Add(x => x.Now, Start.AddMinutes(5))
        );

        timeline.Find(".cs-jt-question").TextContent.Should().Be("withheld");
        timeline.Find(".cs-jt-answer").TextContent.Should().Be("withheld");
        timeline
            .Markup.Should()
            .NotContain("MARKER-ROUTE-NAME-3310")
            .And.NotContain("MARKER-QUESTION-KEY-3311")
            .And.NotContain("MARKER-ANSWER-3312")
            .And.NotContain("58%");
    }

    [Test]
    public void A_junction_on_a_visible_track_shows_its_name_and_the_track()
    {
        var route = Decision(1, JunctionRunKind.Route, "Acme.Routing.Refund");
        route.Answer = "Approve";
        var onTrack = Junction(2, "ApproveRefund", JunctionRunState.Completed, 5, 6);
        onTrack.TrackPosition = 1;

        var timeline = Render(Run(TrainState.Completed, ended: 10), [route, onTrack]);

        var row = timeline.Find(".cs-jt-row[data-position='2']");
        row.QuerySelector(".cs-jt-title")!.TextContent.Should().Be("ApproveRefund");
        row.QuerySelector(".cs-jt-on-track")!.TextContent.Should().Be("on track of step #1");
        timeline
            .Find(".cs-jt-row[data-position='1']")
            .ClassList.Should()
            .NotContain("cs-jt-row--on-track");
    }

    [Test]
    public void A_failed_junction_shows_its_failure_class_and_exception_type_only()
    {
        const string marker = "MARKER-EXCEPTION-MESSAGE-5521";
        var run = Run(TrainState.Failed, ended: 10);
        // Nothing of the run's own failure, input or output belongs in the timeline.
        run.Input = marker;
        run.Output = marker;
        run.StackTrace = marker;
        var step = Junction(0, "Charge", JunctionRunState.Failed, from: 1, to: 3);
        step.FailureClass = FailureClass.Transient;
        step.FailureException = "HttpRequestException";

        var timeline = Render(run, [step]);

        timeline
            .Find(".cs-jt-failure")
            .TextContent.Trim()
            .Should()
            .Be("Transient · HttpRequestException");
        timeline.Find(".cs-jt-bar").ClassList.Should().Contain("cs-jt--failed");
        timeline.Markup.Should().NotContain(marker);
    }

    [Test]
    public void Question_and_answer_text_is_encoded_not_rendered_as_markup()
    {
        var step = Decision(1, JunctionRunKind.Route, "<img src=x onerror=alert(1)>");
        step.Answer = "<script>alert('answer')</script>";

        var timeline = Render(Run(TrainState.Completed, ended: 10), [step]);

        timeline.FindAll("script").Should().BeEmpty();
        timeline.FindAll("img").Should().BeEmpty();
        timeline.Find(".cs-jt-answer").TextContent.Should().Be("<script>alert('answer')</script>");
    }

    [Test]
    public void The_runs_attempt_is_shown_when_set()
    {
        var step = Junction(0, "Charge", JunctionRunState.Completed, from: 0, to: 1);
        step.Attempt = 3;

        var timeline = Render(Run(TrainState.Completed, ended: 10), [step]);

        timeline.Find(".cs-jt-attempt").TextContent.Should().Contain("attempt 3");
    }

    [Test]
    public void An_ad_hoc_run_shows_no_attempt()
    {
        var timeline = Render(
            Run(TrainState.Completed, ended: 10),
            [Junction(0, "Charge", JunctionRunState.Completed, from: 0, to: 1)]
        );

        timeline.FindAll(".cs-jt-attempt").Should().BeEmpty();
    }

    [Test]
    public async Task The_run_page_shows_that_runs_steps_in_position_order()
    {
        var runId = await SeedRunAsync(r => r.TrainState = TrainState.Completed);
        var otherId = await SeedRunAsync(r => r.TrainState = TrainState.Completed);
        await using (var db = await _data.CreateDbContextAsync(default))
        {
            foreach (
                var (metadataId, position, name) in new[]
                {
                    (runId, 2, "Ship"),
                    (otherId, 0, "SomeoneElsesStep"),
                    (runId, 0, "Load"),
                    (runId, 1, "Charge"),
                }
            )
            {
                var step = Junction(position, name, JunctionRunState.Completed, 0, 1);
                step.MetadataId = metadataId;
                db.JunctionRuns.Add(step);
            }
            await db.SaveChanges(default);
        }

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));

        page.WaitForAssertion(
            () =>
                page.FindAll(".cs-jt-row .cs-jt-title")
                    .Select(n => n.TextContent.Trim())
                    .Should()
                    .Equal("Load", "Charge", "Ship"),
            WaitTimeout
        );
        page.Markup.Should().NotContain("SomeoneElsesStep");
    }

    [TestCase(500, false, TestName = "A_run_with_500_steps_shows_them_all_without_a_note")]
    [TestCase(501, true, TestName = "A_run_with_more_than_500_steps_says_it_shows_the_first_500")]
    public async Task The_run_page_never_truncates_the_timeline_silently(int count, bool truncated)
    {
        var runId = await SeedRunAsync(r => r.TrainState = TrainState.Completed);
        await using (var db = await _data.CreateDbContextAsync(default))
        {
            for (var i = 0; i < count; i++)
            {
                var step = Junction(i, $"Step{i}", JunctionRunState.Completed, 0, 1);
                step.MetadataId = runId;
                db.JunctionRuns.Add(step);
            }
            await db.SaveChanges(default);
        }

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));

        page.WaitForAssertion(
            () => page.FindAll(".cs-jt-row").Should().HaveCount(500),
            WaitTimeout
        );
        if (truncated)
            page.Find(".cs-jt-truncated")
                .TextContent.Should()
                .Contain("Showing the first 500 steps; this run recorded more.");
        else
            page.FindAll(".cs-jt-truncated").Should().BeEmpty();
    }

    [Test]
    public async Task A_finished_runs_timeline_is_read_twice_and_then_neither_read_nor_rendered()
    {
        var settings = UseCountingPolls();
        var runId = await SeedRunAsync(r =>
        {
            r.TrainState = TrainState.Completed;
            r.EndTime = r.StartTime.AddSeconds(10);
        });
        await AddStepsAsync(runId, (0, "Load", JunctionRunState.Completed));

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));
        page.WaitForAssertion(() => Titles(page).Should().Equal("Load"), WaitTimeout);
        // The first load sees the run finished and the next reads it once more.
        WaitForPolls(page, settings, settings.Polls + 2);
        var renders = page.FindComponent<JunctionTimeline>().RenderCount;

        await RenameStepAsync(runId, 0, "Renamed");
        await AddStepsAsync(runId, (1, "Late", JunctionRunState.Completed));
        WaitForPolls(page, settings, settings.Polls + 5);

        Titles(page).Should().Equal(["Load"], "a finished run's steps are not read again");
        page.FindComponent<JunctionTimeline>()
            .RenderCount.Should()
            .Be(renders, "an unchanged timeline is not rendered again on each poll");
    }

    [Test]
    public async Task A_finished_runs_step_with_no_recorded_end_stays_still_across_polls()
    {
        var settings = UseCountingPolls();
        var runId = await SeedRunAsync(r =>
        {
            r.TrainState = TrainState.Failed;
            r.EndTime = r.StartTime.AddSeconds(10);
        });
        await AddStepsAsync(runId, (0, "Charge", JunctionRunState.InProgress));

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));
        page.WaitForAssertion(() => Titles(page).Should().Equal("Charge"), WaitTimeout);
        WaitForPolls(page, settings, settings.Polls + 2);
        var renders = page.FindComponent<JunctionTimeline>().RenderCount;

        WaitForPolls(page, settings, settings.Polls + 5);

        page.Find(".cs-jt-duration").TextContent.Should().Be("end not recorded");
        page.FindComponent<JunctionTimeline>()
            .RenderCount.Should()
            .Be(renders, "a finished run's timeline is not laid out again on each poll");
    }

    [Test]
    public async Task A_running_runs_poll_rereads_only_new_steps_and_those_still_in_progress()
    {
        var settings = UseCountingPolls();
        var runId = await SeedRunAsync(r => r.TrainState = TrainState.InProgress);
        await AddStepsAsync(
            runId,
            (0, "Load", JunctionRunState.Completed),
            (1, "Charge", JunctionRunState.InProgress)
        );

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));
        page.WaitForAssertion(() => Titles(page).Should().Equal("Load", "Charge"), WaitTimeout);

        // A step that ended is not read again, so a change to its row is not picked up; the step
        // that was running is, and so is a new one.
        await RenameStepAsync(runId, 0, "Renamed");
        await using (var db = await _data.CreateDbContextAsync(default))
        {
            var charge = db.JunctionRuns.Single(r => r.MetadataId == runId && r.Position == 1);
            charge.State = JunctionRunState.Completed;
            charge.EndedAt = charge.StartedAt.AddSeconds(1);
            await db.SaveChanges(default);
        }
        await AddStepsAsync(runId, (2, "Ship", JunctionRunState.InProgress));

        page.WaitForAssertion(
            () =>
            {
                Titles(page).Should().Equal("Load", "Charge", "Ship");
                page.Find(".cs-jt-row[data-position='1'] .cs-jt-bar")
                    .ClassList.Should()
                    .Contain("cs-jt--completed");
            },
            WaitTimeout
        );
        WaitForPolls(page, settings, settings.Polls + 2);
        Titles(page).Should().Equal("Load", "Charge", "Ship");
    }

    [Test]
    public async Task A_running_run_that_passes_500_steps_says_it_shows_the_first_500()
    {
        UseCountingPolls();
        var runId = await SeedRunAsync(r => r.TrainState = TrainState.InProgress);
        await AddStepsAsync(
            runId,
            Enumerable
                .Range(0, 500)
                .Select(i => (i, $"Step{i}", JunctionRunState.Completed))
                .ToArray()
        );

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));
        page.WaitForAssertion(
            () => page.FindAll(".cs-jt-row").Should().HaveCount(500),
            WaitTimeout
        );
        page.FindAll(".cs-jt-truncated").Should().BeEmpty();

        await AddStepsAsync(runId, (500, "Step500", JunctionRunState.Completed));

        page.WaitForAssertion(
            () =>
                page.Find(".cs-jt-truncated")
                    .TextContent.Should()
                    .Contain("Showing the first 500 steps; this run recorded more."),
            WaitTimeout
        );
        page.FindAll(".cs-jt-row").Should().HaveCount(500);
        page.Markup.Should().NotContain("Step500");
    }

    [Test]
    public async Task The_run_page_hints_at_AddJunctionEvents_when_the_run_has_no_steps()
    {
        var runId = await SeedRunAsync(r => r.TrainState = TrainState.Completed);

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));

        page.WaitForAssertion(
            () => page.Find(".cs-jt-hint").TextContent.Should().Contain("AddJunctionEvents()"),
            WaitTimeout
        );
    }

    [Test]
    public async Task A_retry_links_to_the_run_whose_decisions_it_replays()
    {
        // The scheduler's retry and the requeue both set ReplayDecisionsOf; the page shows either
        // the same way.
        var failedId = await SeedRunAsync(r => r.TrainState = TrainState.Failed);
        var retryId = await SeedRunAsync(r =>
        {
            r.TrainState = TrainState.InProgress;
            r.ReplayDecisionsOf = failedId;
        });

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, retryId));

        page.WaitForAssertion(
            () =>
                page.Find($"a[href='trax/data/metadata/{failedId}']")
                    .TextContent.Should()
                    .Be(failedId.ToString()),
            WaitTimeout
        );
        page.Markup.Should().Contain("Replays Decisions Of");
    }

    [Test]
    public async Task A_run_that_replays_nothing_shows_no_replay_link()
    {
        var runId = await SeedRunAsync(r => r.TrainState = TrainState.Completed);

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));

        page.WaitForAssertion(() => page.Markup.Should().Contain("Executor"), WaitTimeout);
        page.Markup.Should().NotContain("Replays Decisions Of");
    }

    [TestCase(true, "Yes")]
    [TestCase(false, "No (retries ask afresh)")]
    public async Task The_manifest_page_shows_whether_retries_replay_decisions(
        bool replay,
        string expected
    )
    {
        long manifestId;
        await using (var db = await _data.CreateDbContextAsync(default))
        {
            var group = new ManifestGroup { Name = $"replay-{replay}" };
            await db.Track(group);
            await db.SaveChanges(default);
            var manifest = Manifest.Create(new CreateManifest { Name = typeof(IReplayingTrain) });
            manifest.ManifestGroupId = group.Id;
            manifest.ReplayDecisionsOnRetry = replay;
            await db.Track(manifest);
            await db.SaveChanges(default);
            manifestId = manifest.Id;
        }

        var page = _ctx.RenderComponent<ManifestDetailPage>(p =>
            p.Add(x => x.ManifestId, manifestId)
        );

        page.WaitForAssertion(
            () => page.Find(".cs-replay-value").TextContent.Should().Be(expected),
            WaitTimeout
        );
    }

    private CountingPolls UseCountingPolls()
    {
        var settings = new CountingPolls();
        _ctx.Services.AddSingleton<IDashboardSettingsService>(settings);
        return settings;
    }

    private static void WaitForPolls(IRenderedFragment page, CountingPolls settings, int count) =>
        page.WaitForAssertion(
            () => settings.Polls.Should().BeGreaterThanOrEqualTo(count),
            WaitTimeout
        );

    private static IEnumerable<string> Titles(IRenderedFragment page) =>
        page.FindAll(".cs-jt-row .cs-jt-title").Select(n => n.TextContent.Trim());

    private async Task AddStepsAsync(
        long runId,
        params (int Position, string Name, JunctionRunState State)[] steps
    )
    {
        await using var db = await _data.CreateDbContextAsync(default);
        foreach (var (position, name, state) in steps)
        {
            var step = Junction(
                position,
                name,
                state,
                0,
                state == JunctionRunState.InProgress ? null : 1
            );
            step.MetadataId = runId;
            db.JunctionRuns.Add(step);
        }
        await db.SaveChanges(default);
    }

    private async Task RenameStepAsync(long runId, int position, string name)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        db.JunctionRuns.Single(r => r.MetadataId == runId && r.Position == position).Name = name;
        await db.SaveChanges(default);
    }

    // Polls every 10ms and counts the loads that completed.
    private sealed class CountingPolls : IDashboardSettingsService
    {
        private int _polls;

        public int Polls => Volatile.Read(ref _polls);
        public TimeSpan PollingInterval => TimeSpan.FromMilliseconds(10);
        public DateTime LastPollTime { get; private set; } = DateTime.UtcNow;
        public string? LastPollError { get; private set; }
        public bool HideAdminTrains => true;

        public Task InitializeAsync() => Task.CompletedTask;

        public Task SetPollingIntervalAsync(int seconds) => Task.CompletedTask;

        public Task SetHideAdminTrainsAsync(bool hide) => Task.CompletedTask;

        public void NotifyPolled()
        {
            LastPollTime = DateTime.UtcNow;
            LastPollError = null;
            Interlocked.Increment(ref _polls);
        }

        public void NotifyPollFailed(string message) => LastPollError = message;

        public bool ShowSummaryCards => true;
        public bool ShowExecutionsChart => true;
        public bool ShowFailures => true;
        public bool ShowAvgDuration => true;
        public bool ShowServerHealth => true;

        public Task SetComponentVisibilityAsync(string key, bool visible) => Task.CompletedTask;
    }

    private IRenderedComponent<JunctionTimeline> Render(
        Metadata run,
        IReadOnlyList<JunctionRun> steps,
        DateTime? now = null
    ) =>
        _ctx.RenderComponent<JunctionTimeline>(p =>
            p.Add(x => x.Metadata, run)
                // Mapped as the page maps them, through the API's JunctionStep.
                .Add(x => x.Steps, steps.Select(JunctionStep.From).ToList())
                .Add(x => x.Now, now ?? Start.AddMinutes(5))
        );

    private static Metadata Run(TrainState state, int? ended)
    {
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = "Acme.IOrderTrain",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        run.TrainState = state;
        run.StartTime = Start;
        run.EndTime = ended is { } s ? Start.AddSeconds(s) : null;
        return run;
    }

    private static JunctionRun Junction(
        int position,
        string name,
        JunctionRunState state,
        int from,
        int? to
    ) =>
        new()
        {
            Position = position,
            Kind = JunctionRunKind.Junction,
            Name = name,
            State = state,
            StartedAt = Start.AddSeconds(from),
            EndedAt = to is { } t ? Start.AddSeconds(t) : null,
        };

    private static JunctionRun Decision(int position, JunctionRunKind kind, string key) =>
        new()
        {
            Position = position,
            Kind = kind,
            Name = key,
            QuestionKey = key,
            State = JunctionRunState.Completed,
            StartedAt = Start.AddSeconds(4),
        };

    private async Task<long> SeedRunAsync(Action<Metadata> configure)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IReplayingTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        configure(run);
        await db.Track(run);
        await db.SaveChanges(default);
        return run.Id;
    }

    private interface IReplayingTrain;
}
