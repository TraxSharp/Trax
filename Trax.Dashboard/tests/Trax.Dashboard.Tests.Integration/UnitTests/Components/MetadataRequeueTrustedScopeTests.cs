using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using LanguageExt;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Api.Services.Authorization;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// Re-queueing a run of a train that carries <c>[TraxAuthorize]</c> from its detail page. A
/// Blazor circuit has no request, so a train authorization check that ran against the caller
/// would refuse every dashboard enqueue; the page enqueues inside the <c>"dashboard"</c> trusted
/// scope instead, because the dashboard is gated as a whole by its host.
///
/// <para>The page, the operations service and the mediator's enqueue are the real ones, over an
/// in-memory store. Only the mediator's run executor and concurrency limiter are absent, and an
/// enqueue uses neither. Both ways a host can be set up are covered: with the API's train
/// authorization service, which refuses a caller with no request, and with none, where the
/// mediator fails closed. Removing the trusted scope from the page fails both.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0017-a-callers-enqueue-goes-through-the-mediator.md")]
public class MetadataRequeueTrustedScopeTests
{
    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [TestCase(
        true,
        TestName = "Requeue_of_an_authorized_train_succeeds_with_the_API_authorization_service"
    )]
    [TestCase(
        false,
        TestName = "Requeue_of_an_authorized_train_succeeds_with_no_authorization_service"
    )]
    public async Task Requeue_of_an_authorized_train_is_enqueued_as_the_dashboard(
        bool withApiAuthorization
    )
    {
        var trains = new ServiceCollection();
        trains.AddScopedTraxRoute<IGuardedTrain, GuardedTrain>();
        var discovery = new TrainDiscoveryService(trains);
        discovery
            .DiscoverTrains()
            .Should()
            .Contain(
                r => r.ServiceType == typeof(IGuardedTrain) && r.HasAuthorizeAttribute,
                "the premise is a train a caller's authorization would be checked against"
            );

        var services = _ctx.Services;
        services.AddSingleton<IDataContextProviderFactory>(_data);
        services.AddSingleton<ILocalStorageService, InMemoryLocalStorageService>();
        services.AddSingleton<IDashboardSettingsService, DashboardSettingsService>();
        services.AddSingleton<ITrainDiscoveryService>(discovery);
        services.AddSingleton<ITrustedExecutionScope, TrustedExecutionScope>();
        if (withApiAuthorization)
        {
            services.AddLogging();
            services.AddAuthorization();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddScoped<ITrainAuthorizationService, TrainAuthorizationService>();
        }
        services.AddScoped<ITrainExecutionService>(sp => new TrainExecutionService(
            discovery,
            runExecutor: null!,
            concurrencyLimiter: null!,
            _data,
            new MediatorConfiguration(),
            sp
        ));
        services.AddScoped<IOperationsService>(sp => new OperationsService(
            discovery,
            _data,
            new SchedulerConfiguration(),
            sp.GetRequiredService<ITrainExecutionService>()
        ));

        var metadataId = await SeedRunAsync();

        var page = _ctx.RenderComponent<MetadataDetailPage>(p =>
            p.Add(x => x.MetadataId, metadataId)
        );

        var requeue = page.WaitForElement("button:contains('Re-queue')", TimeSpan.FromSeconds(10));
        await requeue.ClickAsync(new());

        var navigation = _ctx.Services.GetRequiredService<FakeNavigationManager>();
        page.WaitForAssertion(
            () =>
                (
                    navigation.Uri.Contains("trax/data/work-queue/")
                    || page.FindAll(".rz-alert").Count > 0
                )
                    .Should()
                    .BeTrue("the re-queue either opens the new entry or reports why it could not"),
            TimeSpan.FromSeconds(10)
        );

        page.FindAll(".rz-alert")
            .Select(a => a.TextContent.Trim())
            .Should()
            .BeEmpty(
                "the dashboard enqueues as trusted infrastructure, so a train's [TraxAuthorize] "
                    + "does not refuse it for lacking a request (docs/0017)"
            );
        navigation.Uri.Should().Contain("trax/data/work-queue/", "a re-queue opens the new entry");

        await using var db = await _data.CreateDbContextAsync(default);
        var queued = await db.WorkQueues.AsNoTracking().ToListAsync();
        queued.Should().ContainSingle().Which.TrainName.Should().Be(typeof(IGuardedTrain).FullName);
    }

    private async Task<long> SeedRunAsync()
    {
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IGuardedTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        run.Input = """{"Value":"again"}""";

        await using var db = await _data.CreateDbContextAsync(default);
        await db.Track(run);
        await db.SaveChanges(default);
        return run.Id;
    }

    public record GuardedInput
    {
        public string Value { get; init; } = "";
    }

    public interface IGuardedTrain : IServiceTrain<GuardedInput, Unit> { }

    [TraxAuthorize(Policy = "GuardedTrainOperators")]
    public class GuardedTrain : ServiceTrain<GuardedInput, Unit>, IGuardedTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}
