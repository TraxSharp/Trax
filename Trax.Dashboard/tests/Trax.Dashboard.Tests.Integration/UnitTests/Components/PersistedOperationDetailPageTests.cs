using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Configuration;
using Trax.Dashboard.Services.Authorization;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.PersistedOperation;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The persisted-operation detail page never ends the operator's circuit. On a host without
/// persisted operations it says the feature is off rather than failing to construct; when the
/// database fails under a read or a write it says so on the page rather than throwing out of a
/// lifecycle method or an event handler, either of which Blazor Server treats as fatal.
/// </summary>
[TestFixture]
public class PersistedOperationDetailPageTests
{
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
        _ctx.Services.AddLogging();
        _ctx.Services.AddSingleton<IDataContextProviderFactory>(_data);
        _ctx.Services.AddSingleton(new DashboardOptions().AllowAnonymousDashboard());
        _ctx.Services.AddScoped<DashboardCircuitAuthorization>();
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void Without_persisted_operations_the_detail_page_says_the_feature_is_not_enabled()
    {
        var page = _ctx.RenderComponent<PersistedOperationDetailPage>(p => p.Add(x => x.Id, Id));

        page.Markup.Should().Contain("Persisted Operations is not enabled on this server");
    }

    [Test]
    public void A_first_load_that_throws_shows_an_error_instead_of_ending_the_circuit()
    {
        var service = UseScriptedService();
        service.Throwing.Add(nameof(IPersistedOperationsService.GetAsync));

        var page = _ctx.RenderComponent<PersistedOperationDetailPage>(p => p.Add(x => x.Id, Id));

        page.WaitForAssertion(() => page.Markup.Should().Contain("Could not load"), Wait);
        page.Markup.Should()
            .NotContain("not found", "a failed read is not reported as a missing row");
    }

    [TestCase(nameof(IPersistedOperationsService.DeactivateAsync), "Deactivate", true)]
    [TestCase(nameof(IPersistedOperationsService.RestoreAsync), "Restore", false)]
    public async Task A_write_that_throws_is_reported_instead_of_ending_the_circuit(
        string method,
        string button,
        bool active
    )
    {
        await SeedAsync(active);
        var service = UseScriptedService();
        service.Throwing.Add(method);
        var page = _ctx.RenderComponent<PersistedOperationDetailPage>(p => p.Add(x => x.Id, Id));

        var click = page.WaitForElement($"button:contains('{button}')", Wait).ClickAsync(new());
        if (active)
        {
            var dialogs = _ctx.Services.GetRequiredService<DialogService>();
            await page.InvokeAsync(() => dialogs.Close("retired"));
        }
        var act = async () => await click;

        await act.Should().NotThrowAsync("an exception out of a handler ends the circuit");
        _ctx.Services.GetRequiredService<NotificationService>()
            .Messages.Should()
            .Contain(m =>
                m.Severity == NotificationSeverity.Error && m.Detail!.Contains("could not be saved")
            );
    }

    [Test]
    public async Task A_reload_after_a_write_that_throws_is_shown_on_the_page()
    {
        await SeedAsync(active: false);
        var service = UseScriptedService();
        var page = _ctx.RenderComponent<PersistedOperationDetailPage>(p => p.Add(x => x.Id, Id));
        var restore = page.WaitForElement("button:contains('Restore')", Wait);

        // The write succeeds, then the database goes away before the page re-reads the row.
        service.Throwing.Add(nameof(IPersistedOperationsService.GetAsync));
        var act = async () => await restore.ClickAsync(new());

        await act.Should().NotThrowAsync();
        page.WaitForAssertion(() => page.Markup.Should().Contain("Could not load"), Wait);
    }

    private ScriptedPersistedOperationsService UseScriptedService()
    {
        var services = _ctx.Services;
        services.AddPersistedOperationStore(store => store.SingleNode());
        var registered = services.Single(d => d.ServiceType == typeof(IPersistedOperationsService));
        services.Remove(registered);
        services.AddSingleton(sp => new ScriptedPersistedOperationsService(
            (IPersistedOperationsService)
                ActivatorUtilities.CreateInstance(sp, registered.ImplementationType!)
        ));
        services.AddSingleton<IPersistedOperationsService>(sp =>
            sp.GetRequiredService<ScriptedPersistedOperationsService>()
        );
        return _ctx.Services.GetRequiredService<ScriptedPersistedOperationsService>();
    }

    private async Task SeedAsync(bool active)
    {
        using var ctx = await _data.CreateDbContextAsync(CancellationToken.None);
        ctx.PersistedOperations.Add(
            new PersistedOperation
            {
                TenantKey = "",
                Id = Id,
                OperationName = "Greet",
                Version = 1,
                Document = "query Greet { a }",
                ShapeFingerprint = new string('a', 64),
                IsActive = active,
                DeprecationReason = active ? null : "retired",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }
        );
        await ctx.SaveChanges(CancellationToken.None);
    }
}
