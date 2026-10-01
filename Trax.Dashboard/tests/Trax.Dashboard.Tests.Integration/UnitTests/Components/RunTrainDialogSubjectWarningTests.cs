using Bunit;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Dialogs;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The Run dialog runs a train now, without a work queue entry, so dispatch never sees the run
/// and a train's <c>QueueSubjectKey</c> is not consulted: the run can overlap queued or in-flight
/// work for the same subject. That is a documented bypass, and for a train that overrides
/// <c>QueueSubjectKey</c> the dialog says so before the user presses Enqueue, pointing them at
/// Queue instead. A train that does not override it is never serialized, so there is nothing to
/// bypass and no warning.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0019-queued-work-for-one-subject-runs-one-at-a-time.md")]
public class RunTrainDialogSubjectWarningTests
{
    private Bunit.TestContext _ctx = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddSingleton(UnusedService<IOperationsService>.Create());
        _ctx.Services.AddSingleton<ITrustedExecutionScope, TrustedExecutionScope>();
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void Run_dialog_for_a_subject_keyed_train_warns_that_it_bypasses_subject_serialization_and_points_to_Queue()
    {
        var dialog = Render<IKeyedTrain, KeyedTrain>();

        var warning = dialog.Find("[data-testid='run-subject-bypass-warning']");
        warning.ClassList.Should().Contain("rz-alert");
        warning
            .TextContent.Should()
            .Contain("subject serialization")
            .And.Contain("concurrently")
            .And.Contain("Queue");
    }

    [Test]
    public void Run_dialog_for_a_train_without_a_subject_key_shows_no_warning()
    {
        var dialog = Render<IUnkeyedTrain, UnkeyedTrain>();

        dialog
            .FindAll("[data-testid='run-subject-bypass-warning']")
            .Should()
            .BeEmpty("a train that never declares a subject has no serialization to bypass");
    }

    private IRenderedComponent<RunTrainDialog> Render<TService, TImpl>()
        where TService : class
        where TImpl : class, TService
    {
        var trains = new ServiceCollection();
        trains.AddScopedTraxRoute<TService, TImpl>();
        var registration = new TrainDiscoveryService(trains)
            .DiscoverTrains()
            .Single(r => r.ServiceType == typeof(TService));

        return _ctx.RenderComponent<RunTrainDialog>(p => p.Add(d => d.Registration, registration));
    }

    public record KeyedInput;

    public record UnkeyedInput;

    public interface IKeyedTrain : IServiceTrain<KeyedInput, Unit> { }

    public class KeyedTrain : ServiceTrain<KeyedInput, Unit>, IKeyedTrain
    {
        protected override string? QueueSubjectKey(Metadata metadata) => "subject";

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface IUnkeyedTrain : IServiceTrain<UnkeyedInput, Unit> { }

    public class UnkeyedTrain : ServiceTrain<UnkeyedInput, Unit>, IUnkeyedTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}
