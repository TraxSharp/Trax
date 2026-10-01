using AngleSharp.Dom;
using Bunit;
using FluentAssertions;

namespace Trax.Dashboard.Tests.Integration.Utils;

/// <summary>
/// Drives a rendered TraxDataGrid the way an operator does: find a row by text it shows, tick
/// its checkbox, page through, and read the batch buttons above it.
/// </summary>
public static class GridInteraction
{
    public static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    public static void WaitForRow(IRenderedFragment page, string text) =>
        page.WaitForAssertion(
            () =>
                page.FindAll("tr.rz-data-row")
                    .Any(r => r.TextContent.Contains(text))
                    .Should()
                    .BeTrue($"a row showing '{text}' is expected"),
            WaitTimeout
        );

    public static IElement Row(IRenderedFragment page, string text) =>
        page.FindAll("tr.rz-data-row").First(r => r.TextContent.Contains(text));

    public static Task ToggleRow(IRenderedFragment page, string text) =>
        Row(page, text).QuerySelector(".rz-chkbox-box")!.ClickAsync(new());

    public static bool IsTicked(IRenderedFragment page, string text) =>
        Row(page, text).QuerySelector(".rz-chkbox-box")!.ClassList.Contains("rz-state-active");

    /// <summary>
    /// Leaves the row ticked or unticked as the operator sees it: clicks it once, or twice when
    /// the row shows the opposite of what one click would give.
    /// </summary>
    public static async Task SetRowTicked(IRenderedFragment page, string text, bool ticked)
    {
        if (IsTicked(page, text) == ticked)
            return;
        await ToggleRow(page, text);
        if (IsTicked(page, text) != ticked)
            await ToggleRow(page, text);
    }

    public static async Task NextPage(IRenderedFragment page, string rowOnNextPage)
    {
        await page.Find(".rz-pager-next").ClickAsync(new());
        WaitForRow(page, rowOnNextPage);
    }

    public static async Task PreviousPage(IRenderedFragment page, string rowOnPreviousPage)
    {
        await page.Find(".rz-pager-prev").ClickAsync(new());
        WaitForRow(page, rowOnPreviousPage);
    }

    /// <summary>The text of the first button whose label contains <paramref name="label"/>, or "".</summary>
    public static string ButtonText(IRenderedFragment page, string label) =>
        page.FindAll("button")
            .Select(b => b.TextContent.Trim())
            .FirstOrDefault(t => t.Contains(label))
        ?? "";

    public static Task ClickButton(IRenderedFragment page, string label) =>
        page.FindAll("button").First(b => b.TextContent.Contains(label)).ClickAsync(new());
}
