using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Pages;
using Trax.Scheduler.Services.Operations;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// The System area: the scheduler's switches are Pause and Resume actions an operator confirms, each settings
/// section saves on its own, the Hosts page reads the heartbeat, and Logs keeps its filters in the URL.
/// </summary>
[TestFixture]
public class SystemBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/system/scheduler")]
    [Covers("mutation:UpdateScheduler")]
    public async Task A_paused_dispatcher_is_resumed_with_one_confirmed_mutation_and_the_header_badge_goes()
    {
        await Seed.PausedDispatcherAsync();
        var scheduler = new SchedulerPage(Console);
        await scheduler.OpenAsync(Caller.Operator);

        var badge = Page.GetByRole(AriaRole.Link, new() { Name = "Dispatch paused", Exact = true });
        await Assertions.Expect(badge).ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(scheduler.Loop("Job dispatcher"))
            .ToContainTextAsync("Paused", new() { Timeout = Render });

        var update = Page.WaitForRequestAsync(
            r =>
                r.Url.EndsWith("/trax/graphql")
                && r.PostData?.Contains("\"operationName\":\"UpdateScheduler\"") == true,
            new() { Timeout = Render }
        );
        await scheduler.SwitchAsync("Resume", "Job dispatcher", "starts again");

        var input = (await update)
            .PostDataJSON()!
            .Value.GetProperty("variables")
            .GetProperty("input");
        input
            .EnumerateObject()
            .Select(p => p.Name)
            .Should()
            .Equal(["jobDispatcherEnabled"], "the switch sends itself and no other setting");
        input.GetProperty("jobDispatcherEnabled").GetBoolean().Should().BeTrue();

        await Assertions
            .Expect(scheduler.Button(scheduler.Loop("Job dispatcher"), "Pause job dispatcher"))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions.Expect(badge).ToHaveCountAsync(0, new() { Timeout = Render });
        Config().JobDispatcherEnabled.Should().BeTrue();
        Checks
            .Sent.Count(s => s.OperationName == "UpdateScheduler")
            .Should()
            .Be(1, "one confirmed click sends one update");
    }

    [Test]
    [Covers("route:/system/scheduler")]
    [Covers("mutation:UpdateScheduler")]
    public async Task A_section_saves_only_its_own_fields()
    {
        var before = Config();
        try
        {
            var scheduler = new SchedulerPage(Console);
            await scheduler.OpenAsync(Caller.Operator);
            var retry = scheduler.Section("Retry");
            var retries = retry.GetByLabel("Default max retries", new() { Exact = true });
            await Assertions
                .Expect(retries)
                .ToHaveValueAsync($"{before.DefaultMaxRetries}", new() { Timeout = Render });

            var update = Page.WaitForRequestAsync(
                r =>
                    r.Url.EndsWith("/trax/graphql")
                    && r.PostData?.Contains("\"operationName\":\"UpdateScheduler\"") == true,
                new() { Timeout = Render }
            );
            await retries.FillAsync($"{before.DefaultMaxRetries + 1}");
            await scheduler.Button(retry, "Save retry").ClickAsync();

            var input = (await update)
                .PostDataJSON()!
                .Value.GetProperty("variables")
                .GetProperty("input");
            input.EnumerateObject().Select(p => p.Name).Should().Equal(["defaultMaxRetries"]);

            await Wait.UntilAsync(
                () => Task.FromResult(Config().DefaultMaxRetries == before.DefaultMaxRetries + 1),
                BrowserTimeouts.Server,
                "the host runs with the saved retry count"
            );
            var after = Config();
            after.JobDispatcherEnabled.Should().Be(before.JobDispatcherEnabled);
            after.DefaultRetryDelay.Should().Be(before.DefaultRetryDelay);
        }
        finally
        {
            await UpdateAsync(
                new UpdateSchedulerConfigInput(DefaultMaxRetries: before.DefaultMaxRetries)
            );
        }
    }

    [Test]
    [Covers("route:/system/hosts")]
    public async Task The_hosts_page_shows_this_host_online_running_the_dispatcher_and_its_process()
    {
        // A host reports the dispatcher at each beat, and only while it is switched on; a test before this one
        // may have paused and resumed it, so wait for a beat that says it runs.
        await Wait.UntilAsync(
            () =>
                Host.QueryAsync(dc =>
                    dc.HostHeartbeats.AsNoTracking().AnyAsync(h => h.RunsDispatcher)
                ),
            BrowserTimeouts.Server,
            "the host beat as running the job dispatcher"
        );
        var hosts = new HostsPage(Console);
        await hosts.OpenAsync(Caller.Operator);

        var online = hosts.Hosts.GetByRole(AriaRole.Row).Filter(new() { HasText = "Online" });
        await Assertions
            .Expect(online.First)
            .ToContainTextAsync("Job dispatcher", new() { Timeout = Render });
        await Assertions
            .Expect(Page.GetByText("No online host runs the job dispatcher"))
            .ToHaveCountAsync(0);
        await Assertions
            .Expect(hosts.ThisProcess)
            .ToContainTextAsync("Working set", new() { Timeout = Render });
        await Assertions.Expect(hosts.ThisProcess).ToContainTextAsync("Uptime");
    }

    [Test]
    [Covers("route:/system/logs")]
    public async Task The_logs_page_reads_its_filters_from_the_url()
    {
        await Console.OpenAsync("system/logs?level=WARNING", Caller.Operator);
        var logs = new LogsPage(Console);
        await Assertions.Expect(logs.Heading).ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(logs.MinimumLevel)
            .ToHaveValueAsync("WARNING", new() { Timeout = Render });

        await logs.MinimumLevel.SelectOptionAsync(new SelectOptionValue { Label = "Error" });
        await Assertions
            .Expect(Page)
            .ToHaveURLAsync(new System.Text.RegularExpressions.Regex("level=ERROR"));
    }

    [Test]
    [Covers("route:/preferences")]
    public async Task Preferences_names_this_browsers_switches_apart_from_the_schedulers()
    {
        var preferences = new PreferencesPage(Console);
        await preferences.OpenAsync(Caller.Operator);
        await Assertions
            .Expect(preferences.HideSchedulerTrains)
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions.Expect(preferences.RefreshEvery).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText("Polling interval")).ToHaveCountAsync(0);
    }

    private static SchedulerConfigSnapshot Config()
    {
        using var scope = Host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOperationsService>().GetSchedulerConfig();
    }

    private static async Task UpdateAsync(UpdateSchedulerConfigInput input)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<IOperationsService>()
            .UpdateSchedulerConfigAsync(input, CancellationToken.None);
        result.Success.Should().BeTrue(result.Message);
    }
}
