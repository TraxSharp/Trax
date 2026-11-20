using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>Dead letters, with their requeue and acknowledge actions.</summary>
public sealed class DeadLettersPage(ConsolePage console)
    : AreaPage(console, "runs/dead-letters", "Dead letters")
{
    public ILocator StatusFilter => Page.GetByRole(AriaRole.Combobox);

    /// <summary>Shows only the dead letters awaiting an operator.</summary>
    public async Task ShowAwaitingAsync() =>
        await StatusFilter
            .SelectOptionAsync(new SelectOptionValue { Label = "Awaiting intervention" })
            .ConfigureAwait(false);

    /// <summary>Shows every dead letter as its own row instead of grouped by train and reason.</summary>
    public async Task ShowAsListAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "List", Exact = true })
            .ClickAsync()
            .ConfigureAwait(false);

    /// <summary>
    /// The header row of the group of dead letters of <paramref name="train"/> (its short name) that were
    /// dead-lettered for <paramref name="reason"/>, by the checkbox that selects the group.
    /// </summary>
    public ILocator Group(string train, string reason) =>
        Page.GetByRole(AriaRole.Row)
            .Filter(
                new()
                {
                    Has = Page.GetByRole(
                        AriaRole.Checkbox,
                        new() { Name = $"Select group: {train}, {reason}", Exact = true }
                    ),
                }
            );

    /// <summary>Opens a group to its rows.</summary>
    public async Task ExpandAsync(string train, string reason) =>
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = $"Show {train}, {reason}", Exact = true }
            )
            .ClickAsync()
            .ConfigureAwait(false);

    /// <summary>Requeues one dead letter from its row, confirming in the console's dialog.</summary>
    public async Task RequeueAsync(long id)
    {
        await Button(Row(id), "Requeue").ClickAsync().ConfigureAwait(false);
        await Console
            .ConfirmAsync("Requeue dead letter", $"Requeue dead letter #{id}?")
            .ConfigureAwait(false);
    }
}
