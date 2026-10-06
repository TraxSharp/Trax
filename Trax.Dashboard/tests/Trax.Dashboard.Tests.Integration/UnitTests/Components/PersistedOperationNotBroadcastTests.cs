using AwesomeAssertions;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Radzen;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Configuration;
using Trax.Dashboard.Services.Authorization;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.PersistedOperation;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The dashboard declares how its store reaches the other nodes, and a change the store saved
/// but could not send to them comes back as <c>CHANGE_NOT_BROADCAST</c> beside the saved row. The
/// pages show that as a warning carrying the service's message, not as a refusal: the change is
/// the operation's new state.
///
/// <para>Enforces <c>docs/adr/0005-persisted-operations-pages-call-the-shared-service.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0005-persisted-operations-pages-call-the-shared-service.md")]
public class PersistedOperationNotBroadcastTests
{
    private const string Adr =
        " (docs/adr/0005-persisted-operations-pages-call-the-shared-service.md)";

    private const string Id = "greet.v1";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

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
        // A broker that never confirms. Registered first, so the store keeps it.
        services.AddSingleton<IPersistedOperationBroadcaster, UnconfirmedBroadcaster>();
        services.AddPersistedOperationStore(store => store.SingleNode());
        services.AddSingleton<IDataContextProviderFactory>(_data);
        services.AddSingleton(new DashboardOptions().AllowAnonymousDashboard());
        services.AddScoped<DashboardCircuitAuthorization>();
        services.AddSingleton<ILocalStorageService, InMemoryLocalStorageService>();
        services.AddSingleton<IDashboardSettingsService, DashboardSettingsService>();
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task A_deactivation_saved_but_not_broadcast_is_a_warning_not_a_refusal()
    {
        await SeedAsync();
        var page = RenderDetail();

        var deactivate = page.WaitForElement("button:contains('Deactivate')", Wait);
        var click = deactivate.ClickAsync(new());
        var dialogs = _ctx.Services.GetRequiredService<DialogService>();
        await page.InvokeAsync(() => dialogs.Close("retired"));
        await click;

        (await ReadRowAsync()).IsActive.Should().BeFalse("the premise: the change is saved");
        var messages = _ctx.Services.GetRequiredService<NotificationService>().Messages;
        messages.Should().NotContain(m => m.Severity == NotificationSeverity.Error, Adr);
        messages
            .Should()
            .ContainSingle(m => m.Severity == NotificationSeverity.Warning)
            .Which.Detail.Should()
            .Contain("could not be sent to the other nodes", Adr);
    }

    [Test]
    public async Task An_upload_saved_but_not_broadcast_closes_the_editor_with_the_warning()
    {
        _ctx.Services.AddScoped<DialogService, RecordingDialogService>();
        var editor = _ctx.RenderComponent<PersistedOperationEditor>();

        editor.Find("input[name='PersistedOperationId']").Change(Id);
        editor.Find("textarea").Change("query Greet { greeting }");
        await editor.Find("button:contains('Save')").ClickAsync(new());

        (await ReadRowAsync()).Should().NotBeNull("the premise: the upload is saved");
        var dialogs = (RecordingDialogService)_ctx.Services.GetRequiredService<DialogService>();
        dialogs
            .ClosedWith.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<string>(
                "a saved upload is not left open as if refused; the page shows the warning" + Adr
            )
            .Which.Should()
            .Contain("could not be sent to the other nodes");
    }

    private IRenderedComponent<PersistedOperationDetailPage> RenderDetail()
    {
        _ctx.Services.GetRequiredService<FakeNavigationManager>()
            .NavigateTo($"trax/data/persisted-operations/{Id}");
        return _ctx.RenderComponent<PersistedOperationDetailPage>(p => p.Add(x => x.Id, Id));
    }

    private async Task SeedAsync()
    {
        using var ctx = await _data.CreateDbContextAsync(CancellationToken.None);
        ctx.PersistedOperations.Add(
            new PersistedOperation
            {
                TenantKey = "",
                Id = Id,
                OperationName = "Greet",
                Version = 1,
                Document = "query Greet { greeting }",
                ShapeFingerprint = new string('a', 64),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }
        );
        await ctx.SaveChanges(CancellationToken.None);
    }

    private async Task<PersistedOperation> ReadRowAsync()
    {
        using var ctx = await _data.CreateDbContextAsync(CancellationToken.None);
        return await ctx.PersistedOperations.AsNoTracking().SingleAsync();
    }

    private sealed class RecordingDialogService(NavigationManager navigation, IJSRuntime js)
        : DialogService(navigation, js)
    {
        public List<object?> ClosedWith { get; } = [];

        public override void Close(dynamic? result = null)
        {
            ClosedWith.Add((object?)result);
            base.Close((object?)result);
        }
    }

    private sealed class UnconfirmedBroadcaster : IPersistedOperationBroadcaster
    {
        public Task PublishAsync(PersistedOperationChangedMessage message, CancellationToken ct) =>
            throw new TimeoutException("the broker did not confirm the publish");
    }
}
