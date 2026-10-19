using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Host;
using Trax.Samples.ApiDashboard.Browser.Pages;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// The run page in a real browser: a failed run is drawn once, on its chain, opened at the step that failed with
/// its error beside it; the timeline and the graph select the same step; a resume from a step names that step;
/// and a run a machine invoked links to the instance that started it.
/// </summary>
[TestFixture]
public class RunPageBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/runs/:id")]
    public async Task A_failed_run_opens_at_the_failed_step_with_its_error_in_the_panel()
    {
        var failed = await Seed.FailedRunWithCheckpointAsync();
        var page = new RunPage(Console, failed.Id, nameof(ISettleClaimTrain));

        await page.OpenAsync(Caller.Operator);

        var panel = page.StepPanel(nameof(NotifyClaimant));
        await Assertions.Expect(panel).ToBeVisibleAsync(new() { Timeout = Render });
        // A step records its exception's type only; the run's failure, with its message, heads the page.
        await Assertions
            .Expect(panel)
            .ToContainTextAsync(nameof(TimeoutException), new() { Timeout = Render });
        await Assertions
            .Expect(page.Page.GetByText("The mail service did not answer").First)
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(page.Node(nameof(NotifyClaimant)))
            .ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(page.Action("Resume")).ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions.Expect(page.Page.GetByText("Junction progress")).ToHaveCountAsync(0);
    }

    [Test]
    [Covers("route:/runs/:id")]
    public async Task The_timeline_and_the_graph_select_the_same_step()
    {
        var failed = await Seed.FailedRunWithCheckpointAsync();
        var page = new RunPage(Console, failed.Id, nameof(ISettleClaimTrain));
        await page.OpenAsync(Caller.Operator);
        await Assertions
            .Expect(page.TimelineStep(nameof(NotifyClaimant)))
            .ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = Render });

        await page.TimelineStep(nameof(LoadClaim)).ClickAsync();

        await Assertions
            .Expect(page.StepPanel(nameof(LoadClaim)))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(page.Node(nameof(LoadClaim)))
            .ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions
            .Expect(page.TimelineStep(nameof(NotifyClaimant)))
            .ToHaveAttributeAsync("aria-pressed", "false");

        await page.Node(nameof(NotifyClaimant)).ClickAsync();

        await Assertions
            .Expect(page.TimelineStep(nameof(NotifyClaimant)))
            .ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = Render });
        await Assertions
            .Expect(page.StepPanel(nameof(NotifyClaimant)))
            .ToBeVisibleAsync(new() { Timeout = Render });
    }

    [Test]
    [Covers("route:/runs/:id")]
    [Covers("mutation:ResumeExecution")]
    public async Task Resume_from_the_failed_step_queues_a_resume_there_and_opens_its_entry()
    {
        var failed = await Seed.FailedRunWithCheckpointAsync();
        var page = new RunPage(Console, failed.Id, nameof(ISettleClaimTrain));
        await page.OpenAsync(Caller.Operator);
        var panel = page.StepPanel(nameof(NotifyClaimant));
        await Assertions.Expect(panel).ToBeVisibleAsync(new() { Timeout = Render });

        await panel
            .GetByRole(AriaRole.Button, new() { Name = "Resume from here", Exact = true })
            .ClickAsync();
        await Console.ConfirmAsync("Resume run", $"Resume run #{failed.Id} from NotifyClaimant");

        await Assertions
            .Expect(Page)
            .ToHaveURLAsync(
                new System.Text.RegularExpressions.Regex(@"/runs/queued/\d+$"),
                new() { Timeout = Render }
            );
        var resumes = Checks.Sent.Where(s => s.OperationName == "ResumeExecution").ToList();
        resumes.Should().ContainSingle("one click sends one resume");
        resumes[0]
            .Raw.Should()
            .Contain("NotifyClaimant", "the resume names the step it was asked from");
    }

    [Test]
    [Covers("route:/runs/:id")]
    public async Task A_run_a_machine_invoked_links_to_the_instance_that_started_it()
    {
        var (instance, run) = await Seed.RunInvokedByMachineAsync();
        var page = new RunPage(Console, run.Id, nameof(IRelayParcelTrain));
        await page.OpenAsync(Caller.Operator);

        var link = page.Link($"Machine instance {RelayMachine.Id} {instance.Id}");
        await Assertions.Expect(link).ToBeVisibleAsync(new() { Timeout = Render });
        await link.ClickAsync();

        await Assertions
            .Expect(Page)
            .ToHaveURLAsync(
                new System.Text.RegularExpressions.Regex(
                    $"/machines/{RelayMachine.Id}/system/{instance.Id}$"
                ),
                new() { Timeout = Render }
            );
        await Assertions
            .Expect(Console.Heading(RelayMachine.Id))
            .ToBeVisibleAsync(new() { Timeout = Render });
    }
}
