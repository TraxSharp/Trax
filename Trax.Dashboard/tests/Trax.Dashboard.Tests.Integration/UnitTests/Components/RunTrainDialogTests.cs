using System.Text.Json;
using Bunit;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Dialogs;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The Run dialog builds a train's input from its Form or JSON tab, writes the run's metadata
/// row and hands both to the job submitter. The Form tab reads its input with the same
/// serializer options a host configures for train parameters, and a submit the job submitter
/// refuses does not leave the row it wrote waiting as Pending for a job that never existed.
///
/// <para>Both tabs read property names in any case and refuse a property given twice, as
/// <c>Trax.Docs/adr/0023-caller-supplied-train-input-is-read-case-insensitively.md</c> decides
/// for every caller-supplied train input.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0023-caller-supplied-train-input-is-read-case-insensitively.md")]
public class RunTrainDialogTests
{
    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private JsonSerializerOptions _previousOptions = null!;

    [SetUp]
    public void SetUp()
    {
        // A host's Trax.Effect build sets these to the train parameter options; a bare test
        // process leaves them at JsonSerializerOptions.Default.
        _previousOptions = TraxEffectConfiguration.StaticSystemJsonSerializerOptions;
        TraxEffectConfiguration.StaticSystemJsonSerializerOptions =
            TraxJsonSerializationOptions.Default;

        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _ctx.Services.AddSingleton<IDataContextProviderFactory>(_data);
    }

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
        TraxEffectConfiguration.StaticSystemJsonSerializerOptions = _previousOptions;
    }

    [Test]
    public async Task Form_tab_submits_an_enum_input()
    {
        var submitter = new RecordingJobSubmitter();
        _ctx.Services.AddSingleton<IJobSubmitter>(submitter);

        var dialog = RenderDialog();
        await ClickEnqueue(dialog);

        dialog.FindAll(ErrorAlerts).Select(a => a.TextContent.Trim()).Should().BeEmpty();
        submitter
            .Input.Should()
            .BeEquivalentTo(
                new ModeInput { Mode = RunMode.Fast, Label = "" },
                "the form's first enum name is its initial value, and it reaches the train"
            );
    }

    [Test]
    public async Task Json_tab_reads_property_names_in_any_case()
    {
        var submitter = new RecordingJobSubmitter();
        _ctx.Services.AddSingleton<IJobSubmitter>(submitter);

        var dialog = RenderDialog();
        await dialog
            .FindAll("button[role=tab]")
            .Single(a => a.TextContent.Contains("JSON"))
            .ClickAsync(new());
        dialog.Find("textarea").Change("""{"Mode":"Slow","Label":"from json"}""");
        await ClickEnqueue(dialog);

        dialog.FindAll(ErrorAlerts).Select(a => a.TextContent.Trim()).Should().BeEmpty();
        submitter
            .Input.Should()
            .BeEquivalentTo(
                new ModeInput { Mode = RunMode.Slow, Label = "from json" },
                "the C# property names read the same as their camelCase form"
            );
    }

    [Test]
    public async Task Submit_passes_the_dialog_cancellation_token()
    {
        var submitter = new RecordingJobSubmitter();
        _ctx.Services.AddSingleton<IJobSubmitter>(submitter);

        var dialog = RenderDialog();
        await ClickEnqueue(dialog);

        submitter
            .Token.CanBeCanceled.Should()
            .BeTrue("closing the dialog cancels a submit that is still in flight");
    }

    [Test]
    public async Task Failed_submit_leaves_no_pending_run()
    {
        _ctx.Services.AddSingleton<IJobSubmitter>(new ThrowingJobSubmitter());

        var dialog = RenderDialog();
        await ClickEnqueue(dialog);

        dialog.WaitForAssertion(
            () =>
                dialog
                    .FindAll(ErrorAlerts)
                    .Select(a => a.TextContent)
                    .Should()
                    .ContainSingle()
                    .Which.Should()
                    .Contain("submitter refused the job"),
            TimeSpan.FromSeconds(10)
        );

        await using var db = await _data.CreateDbContextAsync(default);
        var run = (await db.Metadatas.AsNoTracking().ToListAsync()).Should().ContainSingle().Which;
        run.TrainState.Should()
            .Be(TrainState.Failed, "no job exists to move a Pending run on, so it failed here");
        run.EndTime.Should().NotBeNull();
        run.FailureReason.Should().Contain("submitter refused the job");
    }

    [Test]
    public async Task Json_tab_refuses_a_property_given_twice_in_different_cases()
    {
        var submitter = new RecordingJobSubmitter();
        _ctx.Services.AddSingleton<IJobSubmitter>(submitter);

        var dialog = RenderDialog(typeof(IAmountTrain));
        await dialog
            .FindAll("button[role=tab]")
            .Single(a => a.TextContent.Contains("JSON"))
            .ClickAsync(new());
        dialog.Find("textarea").Change("""{"amount":1,"Amount":999}""");
        await ClickEnqueue(dialog);

        AssertRefusedAsDuplicate(dialog, submitter);
    }

    [Test]
    public async Task Form_tab_refuses_a_property_given_twice_in_different_cases()
    {
        var submitter = new RecordingJobSubmitter();
        _ctx.Services.AddSingleton<IJobSubmitter>(submitter);

        var dialog = RenderDialog(typeof(IAmountTrain));
        // A complex property's form field takes JSON, so the duplicate reaches the reader there.
        dialog.FindAll("input").Last().Change("""{"amount":1,"Amount":999}""");
        await ClickEnqueue(dialog);

        AssertRefusedAsDuplicate(dialog, submitter);
    }

    private void AssertRefusedAsDuplicate(
        IRenderedComponent<RunTrainDialog> dialog,
        RecordingJobSubmitter submitter
    )
    {
        dialog
            .FindAll(ErrorAlerts)
            .Select(a => a.TextContent)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain(
                "Invalid JSON",
                "a property given twice is ambiguous, so it is refused rather than resolved to "
                    + "one of its values (Trax.Docs/adr/0023-caller-supplied-train-input-is-read-case-insensitively.md)"
            )
            .And.ContainEquivalentOf("duplicate");
        submitter.Input.Should().BeNull("nothing is submitted when the input is refused");
    }

    private IRenderedComponent<RunTrainDialog> RenderDialog(Type? serviceType = null)
    {
        serviceType ??= typeof(IModeTrain);
        var trains = new ServiceCollection();
        trains.AddScopedTraxRoute<IModeTrain, ModeTrain>();
        trains.AddScopedTraxRoute<IAmountTrain, AmountTrain>();
        var registration = new TrainDiscoveryService(trains)
            .DiscoverTrains()
            .Single(r => r.ServiceType == serviceType);

        return _ctx.RenderComponent<RunTrainDialog>(p => p.Add(x => x.Registration, registration));
    }

    // The dialog always shows the subject-serialization warning as an alert of its own; these
    // tests are about the error alert beside it.
    private const string ErrorAlerts = ".rz-alert:not([data-testid='run-subject-bypass-warning'])";

    private static async Task ClickEnqueue(IRenderedComponent<RunTrainDialog> dialog)
    {
        var enqueue = dialog.FindAll("button").Single(b => b.TextContent.Contains("Enqueue"));
        await enqueue.ClickAsync(new());
    }

    public enum RunMode
    {
        Fast = 1,
        Slow = 2,
    }

    public record ModeInput
    {
        public RunMode Mode { get; init; }
        public string Label { get; init; } = "unset";
    }

    public record AmountInput
    {
        public int Amount { get; init; }
        public AmountInput? Nested { get; init; }
    }

    public interface IAmountTrain : IServiceTrain<AmountInput, Unit> { }

    public class AmountTrain : ServiceTrain<AmountInput, Unit>, IAmountTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface IModeTrain : IServiceTrain<ModeInput, Unit> { }

    public class ModeTrain : ServiceTrain<ModeInput, Unit>, IModeTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    private sealed class RecordingJobSubmitter : IJobSubmitter
    {
        public object? Input { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<string> EnqueueAsync(long metadataId) =>
            throw new NotSupportedException("the Run dialog always passes an input");

        public Task<string> EnqueueAsync(long metadataId, object input) =>
            EnqueueAsync(metadataId, input, CancellationToken.None);

        public Task<string> EnqueueAsync(
            long metadataId,
            object input,
            CancellationToken cancellationToken
        )
        {
            Input = input;
            Token = cancellationToken;
            return Task.FromResult("job-1");
        }
    }

    private sealed class ThrowingJobSubmitter : IJobSubmitter
    {
        public Task<string> EnqueueAsync(long metadataId) =>
            throw new InvalidOperationException("submitter refused the job");

        public Task<string> EnqueueAsync(long metadataId, object input) =>
            throw new InvalidOperationException("submitter refused the job");
    }
}
