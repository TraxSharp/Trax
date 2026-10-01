using System.Text.Json;
using Bunit;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Dialogs;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The Queue dialog's Form tab builds the input JSON from the form and hands it to the shared
/// operations service, which reads it with the host's train parameter options. The keys it
/// writes are the names those options expect, so every value typed into the form reaches the
/// queued input. A required field left blank is refused on that field rather than queued as a
/// zero, and a blank optional field is queued as no value. The operations service and the
/// mediator's enqueue are the real ones, over an in-memory store.
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
        var dialog = Render<IGreetTrain, GreetTrain>();

        var fields = dialog.FindAll("input.rz-textbox");
        fields.Should().HaveCount(2, "one text field per input property");
        fields.First().Change("hello");
        dialog.FindAll("input.rz-textbox").Last().Change("3");

        await ClickQueue(dialog);

        dialog.FindAll(".rz-alert").Select(a => a.TextContent.Trim()).Should().BeEmpty();
        JsonSerializer
            .Deserialize<GreetInput>(
                await SingleQueuedInput(),
                TraxJsonSerializationOptions.ManifestProperties
            )
            .Should()
            .BeEquivalentTo(
                new GreetInput { Name = "hello", Count = 3 },
                "every value typed into the form is part of the input the train is queued with"
            );
    }

    [Test]
    public async Task A_blank_required_field_is_refused_on_that_field_and_nothing_is_queued()
    {
        var dialog = Render<IOrderTrain, OrderTrain>();

        await ClickQueue(dialog);

        dialog
            .Find("[data-testid='field-error-OrderId']")
            .TextContent.Should()
            .Contain("required", "a blank Guid has no value to send, and 0 is not one");
        dialog
            .Find("[data-testid='field-error-Quantity']")
            .TextContent.Should()
            .Contain("required", "a blank number is not zero");
        await using var db = await _data.CreateDbContextAsync(default);
        (await db.WorkQueues.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task A_blank_optional_field_is_queued_as_no_value()
    {
        var orderId = Guid.NewGuid();
        var dialog = Render<IOrderTrain, OrderTrain>();

        var fields = dialog.FindAll("input.rz-textbox");
        fields.ElementAt(0).Change(orderId.ToString());
        dialog.FindAll("input.rz-textbox").ElementAt(1).Change("2");

        await ClickQueue(dialog);

        dialog.FindAll(".rz-alert").Select(a => a.TextContent.Trim()).Should().BeEmpty();
        JsonSerializer
            .Deserialize<OrderInput>(
                await SingleQueuedInput(),
                TraxJsonSerializationOptions.ManifestProperties
            )
            .Should()
            .BeEquivalentTo(
                new OrderInput
                {
                    OrderId = orderId,
                    Quantity = 2,
                    DeliverBy = null,
                }
            );
    }

    private IRenderedComponent<QueueTrainDialog> Render<TService, TImpl>()
        where TService : class
        where TImpl : class, TService
    {
        var services = _ctx.Services;
        services.AddScopedTraxRoute<TService, TImpl>();
        var discovery = new TrainDiscoveryService(services);
        services.AddRealOperationsService(discovery, _data);
        var registration = discovery
            .DiscoverTrains()
            .Single(r => r.ServiceType == typeof(TService));

        return _ctx.RenderComponent<QueueTrainDialog>(p =>
            p.Add(x => x.Registration, registration)
        );
    }

    private static Task ClickQueue(IRenderedComponent<QueueTrainDialog> dialog) =>
        dialog.FindAll("button").Single(b => b.TextContent.Contains("Queue")).ClickAsync(new());

    private async Task<string> SingleQueuedInput()
    {
        await using var db = await _data.CreateDbContextAsync(default);
        return (await db.WorkQueues.AsNoTracking().ToListAsync())
            .Should()
            .ContainSingle()
            .Which.Input!;
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

    public record OrderInput
    {
        public Guid OrderId { get; init; }
        public int Quantity { get; init; }
        public DateTime? DeliverBy { get; init; }
    }

    public interface IOrderTrain : IServiceTrain<OrderInput, Unit> { }

    public class OrderTrain : ServiceTrain<OrderInput, Unit>, IOrderTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}
