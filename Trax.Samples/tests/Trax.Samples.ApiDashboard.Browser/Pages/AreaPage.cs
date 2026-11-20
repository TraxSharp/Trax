using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>An area of the console at one route, drawn once its heading shows.</summary>
public abstract class AreaPage(ConsolePage console, string route, string heading)
{
    public ConsolePage Console { get; } = console;

    public IPage Page => Console.Page;

    public string Route { get; } = route;

    public ILocator Heading => Console.Heading(heading);

    /// <summary>Loads the area, signs in as <paramref name="caller"/>, and waits for its heading.</summary>
    public async Task OpenAsync(Caller caller)
    {
        await Console.OpenAsync(Route, caller).ConfigureAwait(false);
        await Assertions
            .Expect(Heading)
            .ToBeVisibleAsync(new() { Timeout = BrowserTimeouts.Render.Ms() })
            .ConfigureAwait(false);
    }

    /// <summary>The row of the item with this id, by the checkbox the console labels <c>Select #id</c>.</summary>
    public ILocator Row(long id) => Console.RowSelectable(id);

    /// <summary>What the page says when the operations gate refuses the caller.</summary>
    public ILocator Refusal =>
        Page.GetByText(
            new System.Text.RegularExpressions.Regex(
                "not authori[sz]ed",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            )
        ).First;

    public ILocator Button(ILocator within, string name) =>
        within.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    /// <summary>The page's own content, under the shell's navigation and header.</summary>
    public ILocator Main => Page.GetByRole(AriaRole.Main);

    /// <summary>The value a detail page's field list gives for <paramref name="label"/>.</summary>
    public ILocator DetailField(string label) =>
        Main.Locator($"xpath=.//dt[normalize-space()='{label}']/following-sibling::dd").First;
}
