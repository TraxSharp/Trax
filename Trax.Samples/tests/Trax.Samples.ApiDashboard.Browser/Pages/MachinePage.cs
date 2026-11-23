using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>One machine: its diagram, each state badged with its counts, and its instances under it.</summary>
public sealed class MachinePage : AreaPage
{
    private readonly string machine;

    public MachinePage(ConsolePage console, string machine)
        : base(console, $"machines/{Uri.EscapeDataString(machine)}", machine) =>
        this.machine = machine;

    public ILocator Diagram =>
        Page.GetByRole(AriaRole.Group, new() { Name = $"States of {machine}", Exact = true });

    /// <summary>A state's node: a button named by the state, then its badges.</summary>
    public ILocator State(string state) =>
        Diagram.GetByRole(
            AriaRole.Button,
            new()
            {
                NameRegex = new System.Text.RegularExpressions.Regex(
                    $"^{System.Text.RegularExpressions.Regex.Escape(state)}\\b",
                    System.Text.RegularExpressions.RegexOptions.None,
                    TimeSpan.FromSeconds(1)
                ),
            }
        );

    /// <summary>A badge on a state's node: <c>Instances</c> or <c>Stuck</c>.</summary>
    public ILocator Badge(string state, string badge) =>
        State(state)
            .GetByLabel(
                new System.Text.RegularExpressions.Regex(
                    $"^{System.Text.RegularExpressions.Regex.Escape(badge)}: \\d+$",
                    System.Text.RegularExpressions.RegexOptions.None,
                    TimeSpan.FromSeconds(1)
                )
            );

    public ILocator Instances => Page.GetByRole(AriaRole.Region, new() { Name = "Instances" });

    /// <summary>The link from the machine's states to its board, on the Board page.</summary>
    public ILocator BoardLink =>
        Page.GetByRole(AriaRole.Link, new() { Name = "Its board", Exact = true });

    /// <summary>The machine's board, on the Board page.</summary>
    public ILocator Board =>
        Page.GetByRole(AriaRole.Group, new() { Name = $"Board of {machine}", Exact = true });

    /// <summary>A note on the board: a button named by its label, then its role.</summary>
    public ILocator Sticky(string label) =>
        Board.GetByRole(
            AriaRole.Button,
            new()
            {
                NameRegex = new System.Text.RegularExpressions.Regex(
                    $"^{System.Text.RegularExpressions.Regex.Escape(label)}\\b",
                    System.Text.RegularExpressions.RegexOptions.None,
                    TimeSpan.FromSeconds(1)
                ),
            }
        );

    public ILocator StuckOnly => Page.GetByRole(AriaRole.Checkbox, new() { Name = "Stuck only" });

    /// <summary>The row of one instance, by the link its id is.</summary>
    public ILocator Instance(Guid id) =>
        Instances
            .GetByRole(AriaRole.Row)
            .Filter(
                new()
                {
                    Has = Page.GetByRole(AriaRole.Link, new() { Name = $"{id}", Exact = true }),
                }
            );

    /// <summary>What the page says when the machine's rules cannot be drawn.</summary>
    public ILocator NoDiagram => Page.GetByText($"{machine} has no diagram");
}
