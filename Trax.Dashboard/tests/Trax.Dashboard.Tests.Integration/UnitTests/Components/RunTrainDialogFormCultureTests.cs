using Bunit;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Dialogs;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The Run dialog's Form tab turns what the operator typed into the train's input. The value
/// the operator typed is the value the train receives, whatever culture or time zone the server
/// or the circuit runs in: numbers use the invariant format the dialog's placeholders and its
/// JSON tab use, and a date with no offset is UTC, as the dashboard says every timestamp is. A
/// value that does not read as its field's type is refused on that field, never sent as
/// something else.
/// </summary>
[TestFixture]
public class RunTrainDialogFormCultureTests
{
    private Bunit.TestContext _ctx = null!;
    private CapturingJobSubmitter _submitter = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _submitter = new CapturingJobSubmitter();
        _ctx.Services.AddSingleton<IJobSubmitter>(_submitter);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    [SetCulture("de-DE")]
    public async Task A_decimal_typed_in_the_form_reaches_the_train_unchanged_under_a_German_culture()
    {
        var dialog = Render();

        Field(dialog, "Amount").Change("1.5");
        Field(dialog, "At").Change("2026-09-28 10:00:00");
        await ClickEnqueue(dialog);

        _submitter
            .Inputs.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<AmountInput>()
            .Which.Amount.Should()
            .Be(1.5, "the operator typed 1.5 in the format the dialog's JSON tab uses");
    }

    [Test]
    public async Task A_date_typed_without_an_offset_is_read_as_UTC()
    {
        var dialog = Render();

        Field(dialog, "Amount").Change("1");
        Field(dialog, "At").Change("2026-09-28 10:00:00");
        await ClickEnqueue(dialog);

        var input = _submitter.Inputs.Should().ContainSingle().Which.As<AmountInput>();
        input.At.Should().Be(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));
        input
            .At.Offset.Should()
            .Be(TimeSpan.Zero, "the page promises UTC, not the server's local offset");
    }

    [Test]
    [SetCulture("de-DE")]
    public async Task A_number_written_with_a_comma_is_refused_on_its_field()
    {
        var dialog = Render();

        Field(dialog, "Amount").Change("1,5");
        Field(dialog, "At").Change("2026-09-28 10:00:00");
        await ClickEnqueue(dialog);

        dialog
            .Find("[data-testid='field-error-Amount']")
            .TextContent.Should()
            .Contain("'.' for the decimal point");
        _submitter.Inputs.Should().BeEmpty("a value that does not read is refused, not guessed at");
    }

    private IRenderedComponent<RunTrainDialog> Render()
    {
        var services = _ctx.Services;
        services.AddScopedTraxRoute<IAmountTrain, AmountTrain>();
        var discovery = new TrainDiscoveryService(services);
        services.AddRealOperationsService(discovery, new InMemoryDataContextFactory());
        var registration = discovery
            .DiscoverTrains()
            .Single(r => r.ServiceType == typeof(IAmountTrain));

        return _ctx.RenderComponent<RunTrainDialog>(p => p.Add(x => x.Registration, registration));
    }

    // One text field per property, in declaration order.
    private static AngleSharp.Dom.IElement Field(
        IRenderedComponent<RunTrainDialog> dialog,
        string property
    ) =>
        dialog
            .FindAll(".rz-form-field input.rz-textbox")
            .ElementAt(property == nameof(AmountInput.Amount) ? 0 : 1);

    private static Task ClickEnqueue(IRenderedComponent<RunTrainDialog> dialog) =>
        dialog.FindAll("button").First(b => b.TextContent.Contains("Enqueue")).ClickAsync(new());

    public record AmountInput
    {
        public double Amount { get; init; }
        public DateTimeOffset At { get; init; }
    }

    public interface IAmountTrain : IServiceTrain<AmountInput, Unit> { }

    public class AmountTrain : ServiceTrain<AmountInput, Unit>, IAmountTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    private sealed class CapturingJobSubmitter : IJobSubmitter
    {
        public List<object> Inputs { get; } = [];

        public Task<string> EnqueueAsync(long metadataId) => Task.FromResult("job");

        public Task<string> EnqueueAsync(long metadataId, object input)
        {
            Inputs.Add(input);
            return Task.FromResult("job");
        }
    }
}
