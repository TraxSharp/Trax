using Bunit;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radzen.Blazor;
using Trax.Dashboard.Components.Dialogs;
using Trax.Dashboard.Components.Pages;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The trains page offers Run beside Queue, and Run opens the Run dialog for that train.
/// <c>RunTrainDialog</c> shipped with nothing opening it, so only a host that opened it itself
/// could run a train without queueing it.
/// </summary>
[TestFixture]
public class TrainsPageRunButtonTests
{
    private Bunit.TestContext _ctx = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var trains = new ServiceCollection();
        trains.AddScopedTraxRoute<IPingTrain, PingTrain>();

        var services = _ctx.Services;
        services.AddSingleton<ITrainDiscoveryService>(new TrainDiscoveryService(trains));
        services.AddSingleton<ILocalStorageService, InMemoryLocalStorageService>();
        services.AddSingleton<IDashboardSettingsService, DashboardSettingsService>();
        services.AddSingleton<IDataContextProviderFactory>(new InMemoryDataContextFactory());
        // Opening the dialog runs nothing; it only needs the services it injects.
        services.AddSingleton(UnusedService<IOperationsService>.Create());
        services.AddSingleton<ITrustedExecutionScope, TrustedExecutionScope>();
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void Run_opens_the_run_dialog_for_that_train()
    {
        var dialogs = _ctx.RenderComponent<RadzenDialog>();
        var page = _ctx.RenderComponent<Trains>();

        var run = page.WaitForElement("button:contains('Run')", TimeSpan.FromSeconds(10));
        page.FindAll("button:contains('Queue')")
            .Should()
            .ContainSingle("Run sits beside Queue, it does not replace it");

        run.Click();

        dialogs.WaitForAssertion(
            () =>
                dialogs
                    .FindComponent<RunTrainDialog>()
                    .Instance.Registration.ServiceType.Should()
                    .Be(typeof(IPingTrain)),
            TimeSpan.FromSeconds(10)
        );
        dialogs.Markup.Should().Contain("Run IPingTrain");
    }

    public record PingInput
    {
        public string Value { get; init; } = "";
    }

    public interface IPingTrain : IServiceTrain<PingInput, Unit>;

    public class PingTrain : ServiceTrain<PingInput, Unit>, IPingTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }
}
