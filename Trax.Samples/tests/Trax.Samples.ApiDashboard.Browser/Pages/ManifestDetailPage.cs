using System.Globalization;
using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>One manifest: its schedule, its next runs, its run history and its on/off switch.</summary>
public sealed class ManifestDetailPage(ConsolePage console, long id, string heading)
    : AreaPage(console, string.Create(CultureInfo.InvariantCulture, $"schedules/{id}"), heading)
{
    public ILocator Schedule => Page.GetByRole(AriaRole.Region, new() { Name = "Schedule" });

    public ILocator RunHistory => Page.GetByRole(AriaRole.Region, new() { Name = "Run history" });

    public ILocator EnabledSwitch =>
        Page.GetByRole(AriaRole.Switch, new() { Name = "Manifest enabled", Exact = true });

    /// <summary>Switches the manifest off, confirming in the console's dialog.</summary>
    public async Task DisableAsync()
    {
        await EnabledSwitch.ClickAsync().ConfigureAwait(false);
        await Console
            .ConfirmAsync("Disable manifest", "does not run on its schedule")
            .ConfigureAwait(false);
    }
}
