using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Radzen;
using Trax.Dashboard.Components.Shared;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// A text area whose text can be larger than one SignalR message. Blazor sends a change event's
/// whole value in one hub message, and the component hub refuses anything over 32 KB by closing
/// the circuit, so a pasted train input or GraphQL document over that size lost the operator's
/// dialog. This component has the browser send its text in small interop calls instead
/// (<c>dashboard.js</c>), without raising the host's hub limit.
///
/// <para>The browser half (the window-capture listener that stops Blazor's change event and
/// sends the chunks) cannot run under bUnit; these tests drive the calls it makes.</para>
/// </summary>
[TestFixture]
public class LargeTextAreaTests
{
    private const int HubLimit = 32 * 1024;

    private Bunit.TestContext _ctx = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void It_registers_with_the_browser_script_after_its_first_render()
    {
        var area = Render(_ => { });

        _ctx.JSInterop.VerifyInvoke("traxDashboard.registerLargeText");
        area.Find("textarea").HasAttribute("data-trax-large-text").Should().BeTrue();
    }

    [Test]
    public async Task A_64_KB_text_sent_in_chunks_reaches_the_bound_value_whole()
    {
        string? bound = null;
        var area = Render(v => bound = v);
        var text = Json(64 * 1024);

        await SendAsync(area, id: 1, text);

        bound.Should().Be(text);
        area.Instance.Value.Should().Be(text);
    }

    [Test]
    public async Task Text_over_the_limit_is_refused_and_the_value_kept()
    {
        string? bound = null;
        var area = Render(v => bound = v, maxLength: 10_000);

        await SendAsync(area, id: 1, new string('x', 10_001));

        bound.Should().BeNull("nothing over the limit is taken");
        area.Markup.Should().Contain("was not taken");
    }

    [Test]
    public async Task Chunks_of_an_older_change_are_ignored()
    {
        string? bound = null;
        var area = Render(v => bound = v);

        await area.InvokeAsync(() =>
        {
            area.Instance.BeginText(1, 3);
            area.Instance.AppendText(1, "old");
            area.Instance.BeginText(2, 3);
            area.Instance.AppendText(1, "old");
            area.Instance.AppendText(2, "new");
            return area.Instance.CommitText(2);
        });

        bound.Should().Be("new");
    }

    [Test]
    public void The_change_event_still_carries_text_that_fits_in_one_message()
    {
        string? bound = null;
        var area = Render(v => bound = v);

        area.Find("textarea").Change("{\"a\":1}");

        bound.Should().Be("{\"a\":1}");
    }

    [Test]
    public void A_chunk_of_the_worst_characters_still_fits_in_one_hub_message()
    {
        // A control character is escaped to six characters by JSON.stringify in the browser, and
        // the arguments then travel as a JSON string inside the hub message, which escapes the
        // backslash again: seven bytes for one character.
        var chunk = new string('\u0001', LargeTextArea.ChunkLength);
        var args = JsonSerializer.Serialize(new object[] { long.MaxValue, chunk });
        var message = JsonSerializer.Serialize(
            new object[] { "BeginInvokeDotNetFromJS", "1", null!, "AppendText", 1L, args }
        );

        Encoding.UTF8.GetByteCount(message).Should().BeLessThan(HubLimit);
    }

    private IRenderedComponent<LargeTextArea> Render(Action<string> onChange, int? maxLength = null)
    {
        string? value = null;
        return _ctx.RenderComponent<LargeTextArea>(p =>
        {
            p.Add(x => x.Value, value);
            p.Add(
                x => x.ValueChanged,
                EventCallback.Factory.Create<string>(this, v => onChange(v))
            );
            if (maxLength is not null)
                p.Add(x => x.MaxLength, maxLength.Value);
        });
    }

    // What dashboard.js sends for one change: a begin, the chunks in order, and a commit.
    private static Task SendAsync(IRenderedComponent<LargeTextArea> area, long id, string text) =>
        area.InvokeAsync(() =>
        {
            area.Instance.BeginText(id, text.Length);
            for (var start = 0; start < text.Length; start += LargeTextArea.ChunkLength)
                area.Instance.AppendText(
                    id,
                    text.Substring(start, Math.Min(LargeTextArea.ChunkLength, text.Length - start))
                );
            return area.Instance.CommitText(id);
        });

    private static string Json(int length)
    {
        var builder = new StringBuilder("{\"items\":[");
        var i = 0;
        while (builder.Length < length - 32)
            builder.Append($"{{\"n\":{i++},\"s\":\"émoji 🚂 \\\"quoted\\\"\"}},");
        builder.Append("{}]}");
        return builder.ToString();
    }
}
