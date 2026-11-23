using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>
/// The scheduler's settings: the manifest manager and the job dispatcher paused and resumed one at a time, then
/// a section per concern, each with its own Save.
/// </summary>
public sealed class SchedulerPage(ConsolePage console)
    : AreaPage(console, "system/scheduler", "Scheduler")
{
    /// <summary>The row of one of the scheduler's loops, by its name: "Job dispatcher", "Manifest manager".</summary>
    public ILocator Loop(string name) =>
        Page.GetByRole(AriaRole.Listitem, new() { Name = name, Exact = true });

    /// <summary>A settings section, by its heading: "Retry", "Job timeouts".</summary>
    public ILocator Section(string name) =>
        Page.GetByRole(AriaRole.Region, new() { Name = name, Exact = true });

    /// <summary>Pauses or resumes a loop from its row, confirming in the console's dialog.</summary>
    public async Task SwitchAsync(string verb, string loop, string contains)
    {
        var action = $"{verb} {loop.ToLowerInvariant()}";
        await Button(Loop(loop), action).ClickAsync().ConfigureAwait(false);
        await Console.ConfirmAsync(action, contains).ConfigureAwait(false);
    }
}
