using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NUnit.Framework;
using Radzen;
using Trax.Api.GraphQL.PersistedOperations;
using Trax.Dashboard.Components.Layout.Sidebar;
using Trax.Dashboard.Configuration;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

[TestFixture]
public class PersistedOperationsSidebarTests
{
    private Bunit.TestContext _ctx = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddSingleton(new DashboardOptions());
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void Sidebar_WithoutCapability_HidesPersistedOperationsItem()
    {
        var component = _ctx.RenderComponent<DashboardSidebar>();

        component.Markup.Should().NotContain("Persisted Operations");
        component.Markup.Should().NotContain("/data/persisted-operations");
    }

    [Test]
    public void Sidebar_WithCapability_ShowsPersistedOperationsItem()
    {
        _ctx.Services.AddSingleton<IPersistedOperationsCapability, FakeCapability>();

        var component = _ctx.RenderComponent<DashboardSidebar>();

        component.Markup.Should().Contain("Persisted Operations");
        component.Markup.Should().Contain("/data/persisted-operations");
    }

    private sealed class FakeCapability : IPersistedOperationsCapability { }
}
