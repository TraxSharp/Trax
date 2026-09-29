using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radzen.Blazor;
using Trax.Api.GraphQL.PersistedOperations;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.PersistedOperation;
using PersistedOperationDto = Trax.Api.GraphQL.PersistedOperations.GraphQL.Models.PersistedOperationDto;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The persisted-operations pages address a row by its tenant and its id, as the API's
/// <c>operations.persistedOperations</c> fields do, and read and write through the same
/// resolver methods those fields run (central ADR 0022). The primary key is
/// <c>(tenant_key, id)</c>, so two rows may share an id; each must open, and change, only
/// itself. The list is read one page at a time from the server rather than capped.
///
/// <para>The store is the package's real one, over the in-memory data context.</para>
///
/// <para>Enforces <c>docs/adr/0004-persisted-operations-pages-call-the-api-resolvers.md</c>.</para>
/// </summary>
[TestFixture]
// The ADR guard pairs a class with the last adr property above it, so the local ADR is last.
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[Property("adr", "docs/adr/0004-persisted-operations-pages-call-the-api-resolvers.md")]
public class PersistedOperationsTenantTests
{
    private const string Adr =
        " (docs/adr/0004-persisted-operations-pages-call-the-api-resolvers.md)";

    private const string SharedId = "greet.v1";
    private const string DefaultDocument = "query Greet { defaultTenantField }";
    private const string TenantDocument = "query Greet { acmeTenantField }";

    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();

        var services = _ctx.Services;
        services.AddLogging();
        services.AddPersistedOperationStore("Host=unused");
        // The published AddPersistedOperationStore does not register the internal cache
        // invalidator its store needs outside a GraphQL host, so the test adds it.
        services.AddSingleton(
            typeof(IPersistedOperationStore).Assembly.GetType(
                "Trax.Api.GraphQL.PersistedOperations.Storage.HotChocolateOperationCacheInvalidator",
                throwOnError: true
            )!
        );
        services.AddSingleton<IDataContextProviderFactory>(_data);
        services.AddSingleton<IPersistedOperationsCapability, FakeCapability>();
        services.AddSingleton<ILocalStorageService, InMemoryLocalStorageService>();
        services.AddSingleton<IDashboardSettingsService, DashboardSettingsService>();
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [TestCase(null, DefaultDocument, TenantDocument)]
    [TestCase("acme", TenantDocument, DefaultDocument)]
    public async Task Each_row_sharing_an_id_opens_its_own_detail(
        string? tenant,
        string shown,
        string hidden
    )
    {
        await SeedSharedIdAsync();

        var page = RenderDetail(tenant);

        page.WaitForAssertion(() => page.Markup.Should().Contain(shown, Adr), Wait);
        page.Markup.Should().NotContain(hidden, Adr);
    }

    [TestCase(null)]
    [TestCase("acme")]
    public async Task Deactivating_one_row_leaves_the_other_row_with_the_same_id_active(
        string? tenant
    )
    {
        await SeedSharedIdAsync();
        var page = RenderDetail(tenant);

        var deactivate = page.WaitForElement("button:contains('Deactivate')", Wait);
        var click = deactivate.ClickAsync(new());
        var dialogs = _ctx.Services.GetRequiredService<DialogService>();
        await page.InvokeAsync(() => dialogs.Close("retired"));
        await click;

        var rows = await ReadRowsAsync();
        var target = rows.Single(r => r.TenantKey == (tenant ?? ""));
        var other = rows.Single(r => r.TenantKey != (tenant ?? ""));
        target.IsActive.Should().BeFalse("the row whose page was open was deactivated" + Adr);
        other
            .IsActive.Should()
            .BeTrue("a row with the same id in another tenant is untouched" + Adr);
    }

    [Test]
    public async Task The_list_links_a_tenant_row_to_its_own_tenant()
    {
        await SeedAsync(Row("acme", SharedId, TenantDocument));
        var page = _ctx.RenderComponent<PersistedOperationsPage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("acme"), Wait);
        var view = page.WaitForElement("button:has(i:contains('visibility'))", Wait);
        await view.ClickAsync(new());

        var navigation = _ctx.Services.GetRequiredService<FakeNavigationManager>();
        navigation
            .Uri.Should()
            .EndWith($"trax/data/persisted-operations/{SharedId}?tenant=acme", Adr);
    }

    [Test]
    public async Task The_list_counts_every_row_and_reads_one_page()
    {
        await SeedAsync(
            Enumerable.Range(0, 520).Select(i => Row(null, $"op{i}.v1", "query Q { a }")).ToArray()
        );

        var page = _ctx.RenderComponent<PersistedOperationsPage>();

        page.WaitForAssertion(
            () =>
            {
                var grid = page.FindComponent<RadzenDataGrid<PersistedOperationDto>>().Instance;
                grid.Count.Should().Be(520, "no row is hidden behind a fixed cap" + Adr);
                grid.View.Count()
                    .Should()
                    .BeLessThanOrEqualTo(25, "the server returns one page" + Adr);
            },
            Wait
        );
    }

    [Test]
    public void An_upload_with_no_id_is_refused_with_the_APIs_message()
    {
        var editor = _ctx.RenderComponent<PersistedOperationEditor>();

        editor.Find("button:contains('Save')").Click();

        editor.WaitForAssertion(() => editor.Markup.Should().Contain("id is required.", Adr), Wait);
    }

    [Test]
    public async Task An_upload_into_a_tenant_writes_that_tenants_row()
    {
        var editor = _ctx.RenderComponent<PersistedOperationEditor>();

        editor.Find("input[name='PersistedOperationId']").Change(SharedId);
        editor.Find("input[name='PersistedOperationTenant']").Change("acme");
        editor.Find("textarea").Change(TenantDocument);
        await editor.Find("button:contains('Save')").ClickAsync(new());

        var rows = await ReadRowsAsync();
        rows.Should().ContainSingle();
        rows[0].TenantKey.Should().Be("acme", Adr);
        rows[0].Id.Should().Be(SharedId);
    }

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private IRenderedComponent<PersistedOperationDetailPage> RenderDetail(string? tenant)
    {
        var navigation = _ctx.Services.GetRequiredService<FakeNavigationManager>();
        var query = tenant is null ? "" : $"?tenant={Uri.EscapeDataString(tenant)}";
        navigation.NavigateTo($"trax/data/persisted-operations/{SharedId}{query}");
        return _ctx.RenderComponent<PersistedOperationDetailPage>(p => p.Add(x => x.Id, SharedId));
    }

    private Task SeedSharedIdAsync() =>
        SeedAsync(Row(null, SharedId, DefaultDocument), Row("acme", SharedId, TenantDocument));

    private static PersistedOperation Row(string? tenant, string id, string document) =>
        new()
        {
            TenantKey = tenant ?? "",
            Id = id,
            OperationName = "Greet",
            Version = 1,
            Document = document,
            ShapeFingerprint = new string('a', 64),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

    private async Task SeedAsync(params PersistedOperation[] rows)
    {
        using var ctx = await _data.CreateDbContextAsync(CancellationToken.None);
        ctx.PersistedOperations.AddRange(rows);
        await ctx.SaveChanges(CancellationToken.None);
    }

    private async Task<List<PersistedOperation>> ReadRowsAsync()
    {
        using var ctx = await _data.CreateDbContextAsync(CancellationToken.None);
        return await ctx.PersistedOperations.AsNoTracking().ToListAsync();
    }

    private sealed class FakeCapability : IPersistedOperationsCapability { }
}
