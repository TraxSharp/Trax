using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>One queue entry's page, by its id; the heading is its train's short name.</summary>
public sealed class QueueEntryPage(ConsolePage console, long id, string train)
    : AreaPage(console, $"runs/queued/{id}", train);
