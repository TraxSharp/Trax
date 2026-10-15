using AwesomeAssertions;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radzen.Blazor;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.GraphQL;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Configuration;
using Trax.Dashboard.Services.Authorization;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.PersistedOperation;
using DeactivatePersistedOperationInput = Trax.Api.GraphQL.PersistedOperations.GraphQL.Models.DeactivatePersistedOperationInput;
using PersistedOperationDto = Trax.Api.GraphQL.PersistedOperations.GraphQL.Models.PersistedOperationDto;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The persisted-operations pages address a row by its tenant and its id, as the API's
/// <c>persistedOperations</c> fields do, and read and write through the
/// <c>IPersistedOperationsService</c> those fields call (central ADR 0022). The primary key is
/// <c>(tenant_key, id)</c>, so two rows may share an id; each must open, and change, only
/// itself. The list is read one page at a time from the server rather than capped. The host here
/// registers only <c>AddPersistedOperationStore</c>, as an out-of-process dashboard does, and no
/// GraphQL capability marker.
///
/// <para>The store and service are the package's real ones, over the in-memory data context.</para>
///
/// <para>Enforces <c>docs/adr/0005-persisted-operations-pages-call-the-shared-service.md</c>.</para>
/// </summary>
[TestFixture]
// The ADR guard pairs a class with the last adr property above it, so the local ADR is last.
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[Property("adr", "docs/adr/0005-persisted-operations-pages-call-the-shared-service.md")]
public class PersistedOperationsTenantTests
{
    private const string Adr =
        " (docs/adr/0005-persisted-operations-pages-call-the-shared-service.md)";

    private const string SharedId = "greet.v1";
    private const string DefaultDocument = "query Greet { defaultTenantField }";
    private const string TenantDocument = "query Greet { acmeTenantField }";

    private const string ManagerRole = "Deployer";
    private const string ManagePolicy = "ManagePersistedOperations";

    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private TestAuthorizationContext _auth = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();

        var services = _ctx.Services;
        services.AddLogging();
        // The operator meets the management gate: the dashboard checks it as the API does.
        _auth = _ctx.AddTestAuthorization();
        _auth.SetAuthorized("operator");
        _auth.SetRoles(ManagerRole);
        UseStore(store => store.SingleNode().GateManagement(roles: ManagerRole));
        // Wrap the package's own service, so a test can see that the pages called it.
        var registered = services.Single(d => d.ServiceType == typeof(IPersistedOperationsService));
        services.Remove(registered);
        services.AddSingleton(sp => new ScriptedPersistedOperationsService(
            (IPersistedOperationsService)
                ActivatorUtilities.CreateInstance(sp, registered.ImplementationType!)
        ));
        services.AddSingleton<IPersistedOperationsService>(sp =>
            sp.GetRequiredService<ScriptedPersistedOperationsService>()
        );
        services.AddSingleton<IDataContextProviderFactory>(_data);
        services.AddSingleton(new DashboardOptions().AllowAnonymousDashboard());
        services.AddScoped<DashboardCircuitAuthorization>();
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
    public async Task The_detail_page_reads_and_writes_through_the_registered_service()
    {
        await SeedSharedIdAsync();
        var page = RenderDetail(tenant: null);

        var deactivate = page.WaitForElement("button:contains('Deactivate')", Wait);
        var click = deactivate.ClickAsync(new());
        var dialogs = _ctx.Services.GetRequiredService<DialogService>();
        await page.InvokeAsync(() => dialogs.Close("retired"));
        await click;

        _ctx.Services.GetRequiredService<ScriptedPersistedOperationsService>()
            .Calls.Should()
            .Contain(
                ["GetAsync", "DeactivateAsync"],
                "the host's IPersistedOperationsService, which carries its broadcaster, does the "
                    + "work rather than a store handed to a resolver"
                    + Adr
            );
    }

    [Test]
    public async Task A_deactivation_the_API_refuses_is_reported_with_the_APIs_message()
    {
        await SeedSharedIdAsync();
        var page = RenderDetail(tenant: null);
        var deactivate = page.WaitForElement("button:contains('Deactivate')", Wait);

        // Another writer removes the row after the page loaded it. (Deactivating it twice is not a
        // refusal: the API deactivates again and sends the change to every node again.)
        await DeleteRowsAsync();
        var service = _ctx.Services.GetRequiredService<IPersistedOperationsService>();
        var api = await new PersistedOperationMutations().DeactivatePersistedOperation(
            new DeactivatePersistedOperationInput(SharedId, "retired"),
            service,
            CancellationToken.None
        );
        api.Success.Should().BeFalse("the premise: the API refuses this deactivation");

        var click = deactivate.ClickAsync(new());
        var dialogs = _ctx.Services.GetRequiredService<DialogService>();
        await page.InvokeAsync(() => dialogs.Close("retired"));
        await click;

        _ctx.Services.GetRequiredService<NotificationService>()
            .Messages.Should()
            .ContainSingle(m => m.Severity == NotificationSeverity.Error)
            .Which.Detail.Should()
            .Be(string.Join(" ", api.Errors.Select(e => e.Message)), Adr);
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

    [Test]
    public async Task Without_a_management_gate_the_dashboard_cannot_upload()
    {
        // Documents managed out of band: the host turns on the store and declares no gate.
        UseStore(store => store.SingleNode());

        var list = _ctx.RenderComponent<PersistedOperationsPage>();
        list.WaitForAssertion(
            () => list.Markup.Should().Contain("management is not served on this server"),
            Wait
        );
        list.Markup.Should().NotContain("Upload", "the API serves no management fields here");

        var editor = _ctx.RenderComponent<PersistedOperationEditor>();
        await UploadAsync(editor);

        editor.WaitForAssertion(
            () => editor.Markup.Should().Contain("not authorized to manage persisted operations"),
            Wait
        );
        (await ReadRowsAsync()).Should().BeEmpty("without a gate no surface manages documents");
        _ctx.Services.GetRequiredService<ScriptedPersistedOperationsService>()
            .Calls.Should()
            .NotContain("UploadAsync");
    }

    [Test]
    public async Task An_operator_without_the_management_policy_cannot_upload()
    {
        UseStore(store => store.SingleNode().GateManagement(policy: ManagePolicy));

        var editor = _ctx.RenderComponent<PersistedOperationEditor>();
        await UploadAsync(editor);

        editor.WaitForAssertion(
            () => editor.Markup.Should().Contain("not authorized to manage persisted operations"),
            Wait
        );
        (await ReadRowsAsync())
            .Should()
            .BeEmpty("the operator meets the dashboard's posture but not the management gate");

        _auth.SetPolicies(ManagePolicy);
        await editor.Find("button:contains('Save')").ClickAsync(new());

        (await ReadRowsAsync()).Should().ContainSingle("once the policy is met the upload goes");
    }

    [Test]
    public async Task An_operator_who_loses_the_management_role_cannot_page_the_list()
    {
        await SeedAsync(
            Enumerable.Range(0, 60).Select(i => Row(null, $"op{i}.v1", "query Q { a }")).ToArray()
        );
        var page = _ctx.RenderComponent<PersistedOperationsPage>();
        RadzenDataGrid<PersistedOperationDto> grid = null!;
        page.WaitForAssertion(
            () =>
            {
                grid = page.FindComponent<RadzenDataGrid<PersistedOperationDto>>().Instance;
                grid.Count.Should().Be(60);
            },
            Wait
        );
        var calls = _ctx.Services.GetRequiredService<ScriptedPersistedOperationsService>().Calls;
        var reads = calls.Count(c => c == "ListAsync");

        // The role is taken away while the circuit lives; GraphQL refuses the next request.
        _auth.SetRoles("Reader");
        await page.InvokeAsync(() => grid.GoToPage(1));

        page.WaitForAssertion(
            () => page.Markup.Should().Contain("You do not meet this server's persisted-operation"),
            Wait
        );
        calls
            .Count(c => c == "ListAsync")
            .Should()
            .Be(reads, "every page read checks the management gate again" + Adr);
    }

    // Registered last, so its options are the ones the service and the gate resolve.
    private void UseStore(
        Action<Trax.Api.GraphQL.PersistedOperations.Configuration.PersistedOperationStoreBuilder> configure
    ) => _ctx.Services.AddPersistedOperationStore(configure);

    private static async Task UploadAsync(IRenderedComponent<PersistedOperationEditor> editor)
    {
        editor.Find("input[name='PersistedOperationId']").Change(SharedId);
        editor.Find("textarea").Change(DefaultDocument);
        await editor.Find("button:contains('Save')").ClickAsync(new());
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

    private async Task DeleteRowsAsync()
    {
        using var ctx = await _data.CreateDbContextAsync(CancellationToken.None);
        ctx.PersistedOperations.RemoveRange(await ctx.PersistedOperations.ToListAsync());
        await ctx.SaveChanges(CancellationToken.None);
    }

    private async Task<List<PersistedOperation>> ReadRowsAsync()
    {
        using var ctx = await _data.CreateDbContextAsync(CancellationToken.None);
        return await ctx.PersistedOperations.AsNoTracking().ToListAsync();
    }
}
