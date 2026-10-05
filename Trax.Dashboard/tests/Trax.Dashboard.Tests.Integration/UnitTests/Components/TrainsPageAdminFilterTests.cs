using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Core.Functional;
using Trax.Dashboard.Components.Pages;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// With Hide Administration Trains on, the Trains page hides a train when its interface
/// FullName is one of the scheduler's admin trains, the rule the run and manifest lists and the
/// API's <c>hideAdminTrains</c> filter use. A consumer's train that shares an admin train's
/// short class name is not an admin train. Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md
/// states the principle.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
public class TrainsPageAdminFilterTests
{
    private Bunit.TestContext _ctx = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void Admin_trains_are_hidden_by_full_name_only()
    {
        var trains = new ServiceCollection();
        trains.AddScopedTraxRoute<
            IJobRunnerTrain,
            Trax.Scheduler.Trains.JobRunner.JobRunnerTrain
        >();
        trains.AddScopedTraxRoute<IReportingJobTrain, JobRunnerTrain>();

        _ctx.Services.AddSingleton<ITrainDiscoveryService>(new TrainDiscoveryService(trains));
        _ctx.Services.AddSingleton<ILocalStorageService, InMemoryLocalStorageService>();
        _ctx.Services.AddSingleton<IDashboardSettingsService, DashboardSettingsService>();

        var page = _ctx.RenderComponent<Trains>();

        page.WaitForAssertion(
            () =>
                page.FindAll("tr.rz-data-row")
                    .Select(r => r.TextContent)
                    .Should()
                    .ContainSingle(
                        "the scheduler's JobRunner is hidden, and a consumer's train whose class "
                            + "is also named JobRunnerTrain is not"
                    )
                    .Which.Should()
                    .Contain(nameof(IReportingJobTrain)),
            TimeSpan.FromSeconds(10)
        );
    }

    public record ReportInput;

    public interface IReportingJobTrain : IServiceTrain<ReportInput, Unit> { }

    public class JobRunnerTrain : ServiceTrain<ReportInput, Unit>, IReportingJobTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}
