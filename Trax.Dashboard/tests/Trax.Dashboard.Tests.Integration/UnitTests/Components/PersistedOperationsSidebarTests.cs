using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NUnit.Framework;
using Radzen;
using Trax.Api.GraphQL.PersistedOperations;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Dashboard.Components.Layout.Sidebar;
using Trax.Dashboard.Configuration;
using Trax.Dashboard.Tests.Integration.Fakes.Services;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The sidebar links the persisted-operations pages exactly when the pages can work: when the
/// host registers <c>IPersistedOperationsService</c>, which <c>UsePersistedOperations</c> and
/// <c>AddPersistedOperationStore</c> both do, and declares the management gate. The GraphQL
/// capability marker, which only <c>UsePersistedOperations</c> registers, neither shows nor hides
/// it.
///
/// <para>Enforces <c>docs/adr/0005-persisted-operations-pages-call-the-shared-service.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0005-persisted-operations-pages-call-the-shared-service.md")]
[TestFixture]
public class PersistedOperationsSidebarTests
{
    private const string Adr =
        " (docs/adr/0005-persisted-operations-pages-call-the-shared-service.md)";

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
    public void Sidebar_WithoutService_HidesPersistedOperationsItem()
    {
        var component = _ctx.RenderComponent<DashboardSidebar>();

        component.Markup.Should().NotContain("Persisted Operations");
        component.Markup.Should().NotContain("/data/persisted-operations");
    }

    [Test]
    public void Sidebar_WithOnlyTheCapabilityMarker_HidesPersistedOperationsItem()
    {
        _ctx.Services.AddSingleton<IPersistedOperationsCapability, FakeCapability>();

        var component = _ctx.RenderComponent<DashboardSidebar>();

        component
            .Markup.Should()
            .NotContain("/data/persisted-operations", "the pages need the service" + Adr);
    }

    [Test]
    public void Sidebar_WithServiceButNoManagementGate_HidesPersistedOperationsItem()
    {
        // A host that manages documents out of band: the store, and no GateManagement.
        _ctx.Services.AddSingleton(UnusedService<IPersistedOperationsService>.Create());
        _ctx.Services.AddSingleton<IPersistedOperationsManagementGate>(new FakeGate(false));

        var component = _ctx.RenderComponent<DashboardSidebar>();

        component
            .Markup.Should()
            .NotContain(
                "/data/persisted-operations",
                "management is not served until the host declares who may manage documents"
            );
    }

    [Test]
    public void Sidebar_WithService_ShowsPersistedOperationsItem()
    {
        // What AddPersistedOperationStore registers on a host that serves no GraphQL, with a
        // management gate declared.
        _ctx.Services.AddSingleton(UnusedService<IPersistedOperationsService>.Create());
        _ctx.Services.AddSingleton<IPersistedOperationsManagementGate>(new FakeGate(true));

        var component = _ctx.RenderComponent<DashboardSidebar>();

        component.Markup.Should().Contain("Persisted Operations");
        component
            .Markup.Should()
            .Contain(
                "/data/persisted-operations",
                "AddPersistedOperationStore registers the service without the capability" + Adr
            );
    }

    private sealed class FakeCapability : IPersistedOperationsCapability { }

    private sealed class FakeGate(bool declared) : IPersistedOperationsManagementGate
    {
        public bool IsDeclared => declared;

        public Task<bool> AllowsAsync(
            System.Security.Claims.ClaimsPrincipal user,
            CancellationToken ct = default
        ) => Task.FromResult(declared);
    }
}
