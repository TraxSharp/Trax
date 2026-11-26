using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Scheduler.Services.Operations;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// The Scheduler page's Drain and resume panel: an operator drains every host for a number of minutes through the
/// console's own confirm dialog, the panel and the host's operations service both read the drain, and Resume ends it.
/// The same service a deploy's drain writes through, so what the console shows is what a deploy set. Ends any drain it
/// leaves behind, since every test shares the host.
/// </summary>
[TestFixture]
public class DrainBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/system/scheduler")]
    [Covers("mutation:DrainHosts")]
    [Covers("mutation:ResumeHosts")]
    public async Task An_operator_drains_every_host_from_the_console_and_resumes_them()
    {
        try
        {
            await Console.OpenAsync("system/scheduler", Caller.Operator).ConfigureAwait(false);
            var panel = Page.Locator("#drain");
            await Assertions
                .Expect(panel.GetByText("Taking work"))
                .ToBeVisibleAsync(new() { Timeout = Render })
                .ConfigureAwait(false);

            await panel.GetByLabel("Drain for (minutes)").FillAsync("3").ConfigureAwait(false);
            await ConfirmAsync(panel, "Drain", "Drain every host", "DrainHosts")
                .ConfigureAwait(false);

            await Assertions
                .Expect(panel.GetByText("Drained", new() { Exact = true }))
                .ToBeVisibleAsync(new() { Timeout = Render })
                .ConfigureAwait(false);
            var status = await DrainAsync().ConfigureAwait(false);
            status
                .Drained.Should()
                .BeTrue("the console's Drain goes through the operations service");
            status
                .Until.Should()
                .BeAfter(
                    status.StartedAt ?? DateTime.MinValue,
                    "a drain lasts the minutes it was given"
                );

            await ConfirmAsync(panel, "Resume", "Resume every host", "ResumeHosts")
                .ConfigureAwait(false);

            await Assertions
                .Expect(panel.GetByText("Taking work"))
                .ToBeVisibleAsync(new() { Timeout = Render })
                .ConfigureAwait(false);
            (await DrainAsync().ConfigureAwait(false))
                .Drained.Should()
                .BeFalse("Resume ends the drain for every host");
        }
        finally
        {
            var scope = Host.Services.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
                await scope
                    .ServiceProvider.GetRequiredService<IOperationsService>()
                    .EndDrainAsync(CancellationToken.None)
                    .ConfigureAwait(false);
        }
    }

    /// <summary>Clicks the panel's button, confirms it in the console's dialog, and waits for the operation it sends.</summary>
    private async Task ConfirmAsync(ILocator panel, string button, string dialog, string operation)
    {
        await panel
            .GetByRole(AriaRole.Button, new() { Name = button, Exact = true })
            .ClickAsync()
            .ConfigureAwait(false);
        var sent = Console.SendsAsync(operation);
        await Console
            .Dialog(dialog)
            .GetByRole(AriaRole.Button, new() { Name = button, Exact = true })
            .ClickAsync()
            .ConfigureAwait(false);
        await sent.ConfigureAwait(false);
    }

    private static async Task<DrainStatus> DrainAsync()
    {
        var scope = Host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
            return await scope
                .ServiceProvider.GetRequiredService<IOperationsService>()
                .GetDrainAsync(CancellationToken.None)
                .ConfigureAwait(false);
    }
}
