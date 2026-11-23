using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>One train's page: its chain, its recent runs by step, what starts it, and Run and Queue.</summary>
public sealed partial class TrainPage(ConsolePage console, Type train)
    : AreaPage(console, $"trains/{train.FullName}", ShortName(train))
{
    public ILocator Chain =>
        Page.GetByRole(
            AriaRole.Group,
            new() { Name = $"Chain of {ShortName(train)}", Exact = true }
        );

    public ILocator Step(string name) => Chain.GetByText(name, new() { Exact = true });

    /// <summary>The badge on a step that counts the range's failed runs that stopped there.</summary>
    public ILocator FailureCount => Chain.GetByLabel(FailedRunsLastDay);

    public ILocator Grid =>
        Page.GetByRole(AriaRole.Table, new() { Name = "Recent runs by step", Exact = true });

    /// <summary>The grid's cell for one step of one run, which links the run.</summary>
    public ILocator Cell(long run, string step, string state) =>
        Grid.GetByRole(
            AriaRole.Link,
            new()
            {
                Name = string.Create(CultureInfo.InvariantCulture, $"Run #{run}, {step}: {state}"),
                Exact = true,
            }
        );

    public ILocator NeverRan => Page.GetByText("This train has not run yet.");

    public ILocator Manifests => Page.GetByRole(AriaRole.Region, new() { Name = "Manifests" });

    public ILocator StartedByMachines =>
        Page.GetByRole(AriaRole.Region, new() { Name = "Started by machines" });

    private static string ShortName(Type train) => train.Name;

    [GeneratedRegex(
        @"^Failed runs in the last 24 hours: \d+$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex FailedRunsLastDay { get; }
}
