using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>Persisted operations, served only to a caller who may manage them.</summary>
public sealed class PersistedOperationsPage(ConsolePage console)
    : AreaPage(console, "api-admin/persisted-operations", "Persisted operations")
{
    public ILocator Operation(string id) => Console.RowLinked(id);

    /// <summary>What a caller outside the management gate is told instead of the list.</summary>
    public ILocator NotServed => Page.GetByText("your account may not manage them");
}
