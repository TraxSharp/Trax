using AwesomeAssertions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Dashboard.Components.Dialogs;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The Run dialog builds a train's input from its Form or JSON tab and runs it through the
/// scheduler's operations service inside the <c>"dashboard"</c> trusted scope, the path the API's
/// run takes: the service reads the input, writes the run's row, applies the train's queue hook
/// and submits to the job submitter. The operations service and the mediator are the real ones,
/// over an in-memory store; only the job submitter is recorded.
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
    public async Task Form_tab_submits_an_enum_input_and_opens_the_run()
    {
        var submitter = new RecordingJobSubmitter();
        _ctx.Services.AddSingleton<IJobSubmitter>(submitter);

        var dialog = RenderDialog();
        await ClickEnqueue(dialog);

        dialog.FindAll(".rz-alert").Select(a => a.TextContent.Trim()).Should().BeEmpty();
        submitter
            .Input.Should()
            .BeEquivalentTo(
                new ModeInput { Mode = RunMode.Fast, Label = "" },
                "the form's first enum name is its initial value, and it reaches the train"
            );
        _ctx.Services.GetRequiredService<NavigationManager>()
            .Uri.Should()
            .EndWith($"trax/data/metadata/{submitter.MetadataId}");
    }

    [Test]
    public async Task Json_tab_reads_property_names_in_any_case()
    {
        var submitter = new RecordingJobSubmitter();
        _ctx.Services.AddSingleton<IJobSubmitter>(submitter);

        var dialog = RenderDialog();
        await SwitchToJson(dialog);
        dialog.Find("textarea").Change("""{"Mode":"Slow","Label":"from json"}""");
        await ClickEnqueue(dialog);

        dialog.FindAll(".rz-alert").Select(a => a.TextContent.Trim()).Should().BeEmpty();
        submitter
            .Input.Should()
            .BeEquivalentTo(
                new ModeInput { Mode = RunMode.Slow, Label = "from json" },
                "the C# property names read the same as their camelCase form"
            );
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
                    .FindAll(".rz-alert")
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
            .Be(TrainState.Failed, "no job exists to move a Pending run on, so it failed");
        run.EndTime.Should().NotBeNull();
        run.FailureReason.Should().Contain("submitter refused the job");
    }

    [Test]
    public async Task Json_tab_refuses_a_property_given_twice_in_different_cases()
    {
        var submitter = new RecordingJobSubmitter();
        _ctx.Services.AddSingleton<IJobSubmitter>(submitter);

        var dialog = RenderDialog(typeof(IAmountTrain));
        await SwitchToJson(dialog);
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
        dialog.FindAll("input").First().Change("1");
        // A complex property's form field takes JSON, so the duplicate reaches the reader there.
        dialog.FindAll("input").Last().Change("""{"amount":1,"Amount":999}""");
        await ClickEnqueue(dialog);

        AssertRefusedAsDuplicate(dialog, submitter);
    }

    [Test]
    public async Task A_run_applies_the_trains_OnQueue_hook_and_a_refusal_submits_nothing()
    {
        var submitter = new RecordingJobSubmitter();
        _ctx.Services.AddSingleton<IJobSubmitter>(submitter);

        var dialog = RenderDialog(typeof(IRefusingTrain));
        await ClickEnqueue(dialog);

        dialog
            .FindAll(".rz-alert")
            .Select(a => a.TextContent)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("refused by the hook", "a run gets the per-record checks a queue gets");
        submitter.Input.Should().BeNull("the hook refused the run before anything was submitted");
        await using var db = await _data.CreateDbContextAsync(default);
        (await db.Metadatas.CountAsync()).Should().Be(0, "a refused run writes no row");
    }

    [Test]
    public async Task A_subject_keyed_train_runs_because_the_dialog_is_a_trusted_caller()
    {
        var submitter = new RecordingJobSubmitter();
        _ctx.Services.AddSingleton<IJobSubmitter>(submitter);

        var dialog = RenderDialog(typeof(IKeyedTrain));
        await ClickEnqueue(dialog);

        dialog
            .FindAll(".rz-alert:not([data-testid='run-subject-bypass-warning'])")
            .Select(a => a.TextContent.Trim())
            .Should()
            .BeEmpty(
                "the service runs a subject-keyed train now only for a trusted caller, and the "
                    + "dialog runs inside the dashboard's trusted scope"
            );
        submitter.Input.Should().BeOfType<KeyedInput>();
    }

    private static void AssertRefusedAsDuplicate(
        IRenderedComponent<RunTrainDialog> dialog,
        RecordingJobSubmitter submitter
    )
    {
        dialog
            .FindAll(".rz-alert")
            .Select(a => a.TextContent)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain(
                "Invalid InputJson",
                "a property given twice is ambiguous, so it is refused rather than resolved to "
                    + "one of its values (Trax.Docs/adr/0023-caller-supplied-train-input-is-read-case-insensitively.md)"
            )
            .And.ContainEquivalentOf("duplicate");
        submitter.Input.Should().BeNull("nothing is submitted when the input is refused");
    }

    private IRenderedComponent<RunTrainDialog> RenderDialog(Type? serviceType = null)
    {
        serviceType ??= typeof(IModeTrain);
        var services = _ctx.Services;
        services.AddScopedTraxRoute<IModeTrain, ModeTrain>();
        services.AddScopedTraxRoute<IAmountTrain, AmountTrain>();
        services.AddScopedTraxRoute<IRefusingTrain, RefusingTrain>();
        services.AddScopedTraxRoute<IKeyedTrain, KeyedTrain>();
        var discovery = new TrainDiscoveryService(services);
        services.AddRealOperationsService(discovery, _data);
        var registration = discovery.DiscoverTrains().Single(r => r.ServiceType == serviceType);

        return _ctx.RenderComponent<RunTrainDialog>(p => p.Add(x => x.Registration, registration));
    }

    private static Task SwitchToJson(IRenderedComponent<RunTrainDialog> dialog) =>
        dialog
            .FindAll("button[role=tab]")
            .Single(a => a.TextContent.Contains("JSON"))
            .ClickAsync(new());

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

    public record RefusedInput;

    public record KeyedInput;

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

    public interface IRefusingTrain : IServiceTrain<RefusedInput, Unit> { }

    public class RefusingTrain : ServiceTrain<RefusedInput, Unit>, IRefusingTrain
    {
        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            throw new TrainException("refused by the hook");

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface IKeyedTrain : IServiceTrain<KeyedInput, Unit> { }

    public class KeyedTrain : ServiceTrain<KeyedInput, Unit>, IKeyedTrain
    {
        protected override string? QueueSubjectKey(Metadata metadata) => "subject";

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    private sealed class RecordingJobSubmitter : IJobSubmitter
    {
        public object? Input { get; private set; }
        public long? MetadataId { get; private set; }

        public Task<string> EnqueueAsync(long metadataId) =>
            throw new NotSupportedException("a run always passes an input");

        public Task<string> EnqueueAsync(long metadataId, object input)
        {
            Input = input;
            MetadataId = metadataId;
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
