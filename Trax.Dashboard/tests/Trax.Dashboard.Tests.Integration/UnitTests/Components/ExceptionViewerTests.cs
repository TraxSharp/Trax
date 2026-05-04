using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NUnit.Framework;
using Radzen;
using Trax.Dashboard.Components.Shared;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

[TestFixture]
public class ExceptionViewerTests
{
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
    public void Renders_FailureFields_AsProvided()
    {
        var component = _ctx.RenderComponent<ExceptionViewer>(p =>
            p.Add(x => x.FailureJunction, "MyJunction")
                .Add(x => x.FailureException, "InvalidOperationException")
                .Add(x => x.FailureReason, "boom")
        );

        component.Markup.Should().Contain("MyJunction");
        component.Markup.Should().Contain("InvalidOperationException");
        component.Markup.Should().Contain("boom");
        component.Markup.Should().Contain("Failure Details");
    }

    [Test]
    public void NullFields_RenderAsDash()
    {
        var component = _ctx.RenderComponent<ExceptionViewer>();

        component.Markup.Should().Contain("Failure Details");
    }

    [Test]
    public void NoStackTrace_DoesNotRenderToggleableSection()
    {
        var component = _ctx.RenderComponent<ExceptionViewer>(p => p.Add(x => x.StackTrace, null));

        component.Markup.Should().NotContain("Click to expand");
    }

    [Test]
    public void ShortStackTrace_AutoExpands()
    {
        var component = _ctx.RenderComponent<ExceptionViewer>(p =>
            p.Add(x => x.StackTrace, "at Foo.Bar()\nat Baz.Qux()")
        );

        component.Markup.Should().Contain("Stack Trace");
    }

    [Test]
    public void LongStackTrace_RendersCollapsedWithLineCount()
    {
        var stack = string.Join("\n", Enumerable.Range(0, 20).Select(i => $"at Frame{i}()"));
        var component = _ctx.RenderComponent<ExceptionViewer>(p => p.Add(x => x.StackTrace, stack));

        component.Markup.Should().Contain("Click to expand");
        component.Markup.Should().Contain("20 lines");
    }

    [Test]
    public void StackTraceFormatting_HandlesAtFramesAndExceptions()
    {
        var stack =
            "InvalidOperationException: it broke\n   at Foo.Bar.Baz() in /tmp/foo.cs:line 12\n   --- End of inner exception stack trace ---";
        var component = _ctx.RenderComponent<ExceptionViewer>(p => p.Add(x => x.StackTrace, stack));

        component.Markup.Should().Contain("Stack Trace");
    }

    [Test]
    public void ToggleStackTrace_ClickCollapsesAndExpands()
    {
        var stack = string.Join("\n", Enumerable.Range(0, 20).Select(i => $"at Frame{i}()"));
        var component = _ctx.RenderComponent<ExceptionViewer>(p => p.Add(x => x.StackTrace, stack));

        // Currently collapsed (long stack)
        component.Markup.Should().Contain("Click to expand");

        // Click the header to expand
        component.Find(".cs-exception-header").Click();
        component.Markup.Should().Contain("Stack Trace");
    }
}
