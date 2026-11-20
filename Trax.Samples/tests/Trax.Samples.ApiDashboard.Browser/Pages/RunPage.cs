using System.Globalization;
using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>
/// One run: its header and actions, its chain on the graph with the selected step's panel, and the tabs under it.
/// The heading is the train's short name.
/// </summary>
public sealed class RunPage(ConsolePage console, long id, string train)
    : AreaPage(console, string.Create(CultureInfo.InvariantCulture, $"runs/{id}"), train)
{
    public ILocator Graph => Page.GetByRole(AriaRole.Region, new() { Name = "Run graph" });

    /// <summary>A step's node on the graph, by the name it is drawn with.</summary>
    public ILocator Node(string step) =>
        Graph
            .GetByRole(AriaRole.Group, new() { Name = "Steps of the train" })
            .GetByRole(
                AriaRole.Button,
                new()
                {
                    NameRegex = new System.Text.RegularExpressions.Regex(
                        "^" + System.Text.RegularExpressions.Regex.Escape(step) + " ",
                        System.Text.RegularExpressions.RegexOptions.None,
                        TimeSpan.FromSeconds(1)
                    ),
                }
            );

    /// <summary>The panel of the step selected on the graph.</summary>
    public ILocator StepPanel(string step) =>
        Graph.GetByRole(AriaRole.Complementary, new() { Name = $"Step {step}", Exact = true });

    public ILocator Timeline =>
        Page.GetByRole(AriaRole.Region, new() { Name = "Junction timeline" });

    /// <summary>A step of the timeline that selects its node, by the junction's name.</summary>
    public ILocator TimelineStep(string name) =>
        Timeline.GetByRole(
            AriaRole.Button,
            new()
            {
                NameRegex = new System.Text.RegularExpressions.Regex(
                    @"^Step #\d+ " + System.Text.RegularExpressions.Regex.Escape(name) + "$",
                    System.Text.RegularExpressions.RegexOptions.None,
                    TimeSpan.FromSeconds(1)
                ),
            }
        );

    public ILocator Action(string name) =>
        Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    public ILocator Link(string name) =>
        Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true });

    public ILocator Tab(string name) =>
        Page.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true });
}
