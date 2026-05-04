using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Radzen;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Utilities;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

[TestFixture]
public class DagGraphTests
{
    private Bunit.TestContext _ctx = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void NullLayout_RendersEmptyMessage()
    {
        var component = _ctx.RenderComponent<DagGraph>();

        component.Markup.Should().Contain("No dependency graph to display");
    }

    [Test]
    public void EmptyLayout_RendersEmptyMessage()
    {
        var layout = new DagLayout
        {
            Width = 100,
            Height = 100,
            Nodes = new List<PositionedNode>(),
            Edges = new List<PositionedEdge>(),
        };

        var component = _ctx.RenderComponent<DagGraph>(p => p.Add(x => x.Layout, layout));

        component.Markup.Should().Contain("No dependency graph to display");
    }

    [Test]
    public void LayoutWithNodes_RendersSvgAndNodes()
    {
        var layout = new DagLayout
        {
            Width = 200,
            Height = 200,
            Nodes = new List<PositionedNode>
            {
                new()
                {
                    Id = 1,
                    Label = "A",
                    X = 10,
                    Y = 10,
                    Width = 80,
                    Height = 30,
                },
                new()
                {
                    Id = 2,
                    Label = "B",
                    X = 100,
                    Y = 100,
                    Width = 80,
                    Height = 30,
                    IsHighlighted = true,
                },
            },
            Edges = new List<PositionedEdge> { new() { PathData = "M50 25 L100 100" } },
        };

        var component = _ctx.RenderComponent<DagGraph>(p => p.Add(x => x.Layout, layout));

        component.Markup.Should().Contain("<svg");
        component.Markup.Should().Contain(">A<");
        component.Markup.Should().Contain(">B<");
        component.Markup.Should().Contain("cs-dag-node--highlighted");
        component.Markup.Should().Contain("M50 25 L100 100");
    }

    [Test]
    public void LongLabel_AppliesTextLength()
    {
        var longLabel = new string('X', 30);
        var layout = new DagLayout
        {
            Width = 200,
            Height = 100,
            Nodes = new List<PositionedNode>
            {
                new()
                {
                    Id = 1,
                    Label = longLabel,
                    X = 10,
                    Y = 10,
                    Width = 80,
                    Height = 30,
                },
            },
            Edges = new List<PositionedEdge>(),
        };

        var component = _ctx.RenderComponent<DagGraph>(p => p.Add(x => x.Layout, layout));

        component.Markup.Should().Contain("textLength").And.Contain("lengthAdjust");
    }
}
