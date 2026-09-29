using System.Text.Json;
using Bunit;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Dialogs;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Utils;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The Queue dialog's Form tab builds the input JSON from the form and hands it to the shared
/// operations service, which reads it with the host's train parameter options. The keys it
/// writes are the names those options expect, so every value typed into the form reaches the
/// queued input. The operations service and the mediator's enqueue are the real ones, over an
/// in-memory store.
/// </summary>
[TestFixture]
public class QueueTrainDialogFormTests
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
    public void TearDown()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task Form_values_reach_the_queued_input()
    {
        var trains = new ServiceCollection();
        trains.AddScopedTraxRoute<IGreetTrain, GreetTrain>();
        var discovery = new TrainDiscoveryService(trains);
        var registration = discovery
            .DiscoverTrains()
            .Single(r => r.ServiceType == typeof(IGreetTrain));

        var services = _ctx.Services;
        services.AddSingleton<IDataContextProviderFactory>(_data);
        services.AddSingleton<ITrainDiscoveryService>(discovery);
        services.AddSingleton<ITrustedExecutionScope, TrustedExecutionScope>();
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

        var dialog = _ctx.RenderComponent<QueueTrainDialog>(p =>
            p.Add(x => x.Registration, registration)
        );

        var fields = dialog.FindAll("input.rz-textbox");
        fields.Should().HaveCount(2, "one text field per input property");
        fields.First().Change("hello");
        dialog.FindAll("input.rz-textbox").Last().Change("3");

        await dialog
            .FindAll("button")
            .Single(b => b.TextContent.Contains("Queue"))
            .ClickAsync(new());

        dialog.FindAll(".rz-alert").Select(a => a.TextContent.Trim()).Should().BeEmpty();

        await using var db = await _data.CreateDbContextAsync(default);
        var entry = (await db.WorkQueues.AsNoTracking().ToListAsync())
            .Should()
            .ContainSingle()
            .Which;
        JsonSerializer
            .Deserialize<GreetInput>(entry.Input!, TraxJsonSerializationOptions.ManifestProperties)
            .Should()
            .BeEquivalentTo(
                new GreetInput { Name = "hello", Count = 3 },
                "every value typed into the form is part of the input the train is queued with"
            );
    }

    public record GreetInput
    {
        public string Name { get; init; } = "";
        public int Count { get; init; }
    }

    public interface IGreetTrain : IServiceTrain<GreetInput, Unit> { }

    public class GreetTrain : ServiceTrain<GreetInput, Unit>, IGreetTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}
