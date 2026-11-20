using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>One persisted operation, by its id, in the default tenant: its fields, its document, its history.</summary>
public sealed class PersistedOperationPage(ConsolePage console, string id)
    : AreaPage(console, $"api-admin/persisted-operations/{Uri.EscapeDataString(id)}", id);
