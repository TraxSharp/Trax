using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Radzen;
using Trax.Dashboard.Components.Shared;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

[TestFixture]
public class FieldDisplayTests
{
    private Bunit.TestContext _ctx = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        // Radzen registers a handful of services that components inject implicitly.
        _ctx.Services.AddRadzenComponents();
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void Renders_LabelAndValue()
    {
        var component = _ctx.RenderComponent<FieldDisplay>(parameters =>
            parameters.Add(p => p.Label, "Username").Add(p => p.Value, "alice")
        );

        component.Markup.Should().Contain("Username");
        component.Markup.Should().Contain("alice");
    }

    [Test]
    public void ChildContent_OverridesValue()
    {
        var component = _ctx.RenderComponent<FieldDisplay>(parameters =>
            parameters
                .Add(p => p.Label, "Status")
                .AddChildContent("<span class=\"custom\">custom-value</span>")
        );

        component.Markup.Should().Contain("custom-value");
    }
}
