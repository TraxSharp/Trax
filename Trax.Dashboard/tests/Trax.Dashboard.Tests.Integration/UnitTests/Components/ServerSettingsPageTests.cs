using Bunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Settings;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The Server Settings page edits a copy of the scheduler settings and writes it through the
/// shared operations service on Save, the same write the GraphQL <c>updateSchedulerConfig</c>
/// mutation makes. The live settings move only when that write does, the persisted row is
/// what a restarted host loads, and the page reports what the service said.
/// </summary>
[TestFixture]
public class ServerSettingsPageTests
{
    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private SchedulerConfiguration _config = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _config = new SchedulerConfiguration();
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task A_saved_edit_is_applied_and_persisted()
    {
        // Equal intervals, so the save carries no change other than the edit itself.
        _config.JobDispatcherPollingInterval = _config.ManifestManagerPollingInterval;
        var page = RenderPage();

        page.Find("input[name=DefaultMaxRetries]").Change("7");
        await ClickButton(page, "Save");

        _config.DefaultMaxRetries.Should().Be(7);
        (await PersistedRow())
            .Should()
            .NotBeNull("a restarted host loads its settings from the persisted row")
            .And.Subject.As<Trax.Effect.Models.SchedulerConfig.SchedulerConfig>()
            .DefaultMaxRetries.Should()
            .Be(7);
    }

    [Test]
    public async Task An_unsaved_edit_does_not_apply()
    {
        var page = RenderPage();

        page.Find("input[name=DefaultMaxRetries]").Change("7");

        _config.DefaultMaxRetries.Should().Be(3, "nothing is applied before Save");
        (await PersistedRow()).Should().BeNull();
    }

    [Test]
    public async Task Save_leaves_the_dispatcher_polling_interval_alone()
    {
        _config.JobDispatcherPollingInterval = TimeSpan.FromSeconds(2);
        _config.ManifestManagerPollingInterval = TimeSpan.FromSeconds(5);
        var page = RenderPage();

        page.Find("input[name=DefaultMaxRetries]").Change("7");
        await ClickButton(page, "Save");

        _config
            .JobDispatcherPollingInterval.Should()
            .Be(
                TimeSpan.FromSeconds(2),
                "the page edits the ManifestManager's interval only, and has no field for the dispatcher's"
            );
    }

    [Test]
    public async Task Reset_fills_the_form_and_Save_writes_the_defaults_through_the_service()
    {
        _config.DefaultMaxRetries = 7;
        var page = RenderPage();

        await ClickButton(page, "Reset Default");
        _config.DefaultMaxRetries.Should().Be(7, "Reset only fills the form; Save applies it");
        (await PersistedRow()).Should().BeNull();

        await ClickButton(page, "Save");
        _config.DefaultMaxRetries.Should().Be(new SchedulerConfiguration().DefaultMaxRetries);
        (await PersistedRow())!.DefaultMaxRetries.Should().Be(3);
    }

    [Test]
    public async Task Save_shows_what_the_service_reported()
    {
        var page = RenderPage();

        page.Find("input[name=DefaultMaxRetries]").Change("7");
        await ClickButton(page, "Save");

        _ctx.Services.GetRequiredService<NotificationService>()
            .Messages.Select(m => m.Detail)
            .Should()
            .Contain("Scheduler config: 1 field(s) updated.");
    }

    [Test]
    public async Task A_save_that_fails_is_reported_and_the_form_stays_dirty()
    {
        var page = RenderPage(new UnreachableDataContextFactory());

        page.Find("input[name=DefaultMaxRetries]").Change("7");
        await ClickButton(page, "Save");

        var messages = _ctx.Services.GetRequiredService<NotificationService>().Messages;
        messages.Should().ContainSingle().Which.Severity.Should().Be(NotificationSeverity.Error);
        messages.Single().Detail.Should().Contain("database is unreachable");
        page.FindAll(".cs-fieldset-dirty").Should().NotBeEmpty("the edit was not saved");
    }

    private IRenderedComponent<ServerSettingsPage> RenderPage(
        IDataContextProviderFactory? data = null
    )
    {
        var factory = data ?? _data;
        var discovery = new TrainDiscoveryService(new ServiceCollection());
        var services = _ctx.Services;
        services.AddSingleton(_config);
        services.AddSingleton<IDataContextProviderFactory>(factory);
        services.AddScoped<ITrainExecutionService>(sp => new TrainExecutionService(
            discovery,
            runExecutor: null!,
            concurrencyLimiter: null!,
            factory,
            new MediatorConfiguration(),
            sp
        ));
        services.AddScoped<IOperationsService>(sp => new OperationsService(
            discovery,
            factory,
            _config,
            sp.GetRequiredService<ITrainExecutionService>()
        ));

        return _ctx.RenderComponent<ServerSettingsPage>();
    }

    private static async Task ClickButton(IRenderedComponent<ServerSettingsPage> page, string text)
    {
        var button = page.FindAll("button").Single(b => b.TextContent.Trim().EndsWith(text));
        await button.ClickAsync(new());
    }

    private async Task<Trax.Effect.Models.SchedulerConfig.SchedulerConfig?> PersistedRow()
    {
        await using var db = await _data.CreateDbContextAsync(default);
        return await db.SchedulerConfigs.AsNoTracking().SingleOrDefaultAsync();
    }

    private sealed class UnreachableDataContextFactory : IDataContextProviderFactory
    {
        public IDataContext Create() =>
            throw new InvalidOperationException("database is unreachable");

        public Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("database is unreachable");

        IEffectProvider IEffectProviderFactory.Create() => Create();
    }
}
