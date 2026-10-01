using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectProvider;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// A polling page's first load can fail: the database is restarting, a connection pool is
/// exhausted. The page says so and keeps polling; the failure is not allowed out of the
/// component's lifecycle, where Blazor Server treats it as fatal and tears down the operator's
/// whole circuit (every open dialog, unsaved edit and page state with it).
/// </summary>
[TestFixture]
public class FirstLoadFailureTests
{
    private Bunit.TestContext _ctx = null!;
    private SwitchableDatabase _database = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _database = new SwitchableDatabase();
        _ctx.Services.AddSingleton<IDataContextProviderFactory>(_database);
        _ctx.Services.AddSingleton<ITrainDiscoveryService>(
            new TrainDiscoveryService(new ServiceCollection())
        );
        _ctx.Services.AddSingleton<ILocalStorageService, InMemoryLocalStorageService>();
        _ctx.Services.AddSingleton<IDashboardSettingsService, DashboardSettingsService>();
        _ctx.Services.AddSingleton(UnusedService<IOperationsService>.Create());
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void A_detail_page_whose_first_load_fails_renders_instead_of_throwing()
    {
        _database.Reachable = false;

        var render = () =>
            _ctx.RenderComponent<WorkQueueDetailPage>(p => p.Add(x => x.WorkQueueId, 1));

        render
            .Should()
            .NotThrow(
                "an exception out of OnInitializedAsync ends the Blazor Server circuit; a "
                    + "database blip on first load must not"
            );
    }

    [Test]
    public void A_failed_first_load_says_it_could_not_load_rather_than_not_found()
    {
        _database.Reachable = false;

        var page = _ctx.RenderComponent<WorkQueueDetailPage>(p => p.Add(x => x.WorkQueueId, 1));

        page.WaitForAssertion(
            () => page.Markup.Should().Contain("Could not load work queue entry 1"),
            TimeSpan.FromSeconds(5)
        );
        page.Markup.Should()
            .NotContain("Not Found", "nothing is known about whether the entry exists")
            .And.Contain("database unreachable");
        _ctx.Services.GetRequiredService<IDashboardSettingsService>()
            .LastPollError.Should()
            .Be("database unreachable", "the header's stale-data indicator reports the failure");
    }

    [Test]
    public void A_failed_load_after_a_route_change_renders_instead_of_throwing()
    {
        var page = _ctx.RenderComponent<WorkQueueDetailPage>(p => p.Add(x => x.WorkQueueId, 1));
        page.WaitForAssertion(
            () => page.Markup.Should().Contain("No work queue entry found with ID 1"),
            TimeSpan.FromSeconds(5)
        );

        _database.Reachable = false;
        var navigate = () => page.SetParametersAndRender(p => p.Add(x => x.WorkQueueId, 2));

        navigate
            .Should()
            .NotThrow("a route change reloads in OnParametersSetAsync, also a lifecycle method");
        page.WaitForAssertion(
            () => page.Markup.Should().Contain("Could not load work queue entry 2"),
            TimeSpan.FromSeconds(5)
        );
    }

    [Test]
    public void A_page_whose_first_load_failed_recovers_on_a_later_poll()
    {
        _database.Reachable = false;
        _ctx.Services.GetRequiredService<IDashboardSettingsService>()
            .SetPollingIntervalAsync(1)
            .GetAwaiter()
            .GetResult();

        var page = _ctx.RenderComponent<WorkQueueDetailPage>(p => p.Add(x => x.WorkQueueId, 1));
        page.WaitForAssertion(
            () => page.Markup.Should().Contain("Could not load"),
            TimeSpan.FromSeconds(5)
        );

        _database.Reachable = true;

        page.WaitForAssertion(
            () => page.Markup.Should().Contain("No work queue entry found with ID 1"),
            TimeSpan.FromSeconds(10)
        );
    }

    /// <summary>
    /// The in-memory store, or a database that throws on every connection while
    /// <see cref="Reachable"/> is false.
    /// </summary>
    private sealed class SwitchableDatabase : IDataContextProviderFactory
    {
        private readonly InMemoryDataContextFactory _store = new();

        public bool Reachable { get; set; } = true;

        public Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken) =>
            Reachable
                ? _store.CreateDbContextAsync(cancellationToken)
                : throw new InvalidOperationException("database unreachable");

        IEffectProvider Trax.Effect.Services.EffectProviderFactory.IEffectProviderFactory.Create() =>
            Reachable
                ? (
                    (Trax.Effect.Services.EffectProviderFactory.IEffectProviderFactory)_store
                ).Create()
                : throw new InvalidOperationException("database unreachable");
    }
}
