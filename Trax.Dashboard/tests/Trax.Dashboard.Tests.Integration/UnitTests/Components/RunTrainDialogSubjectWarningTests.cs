using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Dialogs;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Trains;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The Run dialog hands its input straight to the job submitter. No work queue entry is written,
/// so dispatch never sees the run and a train's <c>QueueSubjectKey</c> is not consulted: the run
/// can overlap queued or in-flight work for the same subject. That is a documented bypass, and
/// the dialog says so before the user presses Enqueue, pointing them at Queue instead. Nothing
/// published tells the dashboard whether a train is subject-keyed, so the warning is always shown.
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
        _ctx.Services.AddSingleton<IDataContextProviderFactory>(new InMemoryDataContextFactory());
        _ctx.Services.AddSingleton<IJobSubmitter, UnusedJobSubmitter>();
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void Run_dialog_warns_that_it_bypasses_subject_serialization_and_points_to_Queue()
    {
        var trains = new ServiceCollection();
        trains.AddScopedTraxRoute<IFakeTrainA, FakeTrainA>();
        var registration = new TrainDiscoveryService(trains)
            .DiscoverTrains()
            .Single(r => r.ServiceType == typeof(IFakeTrainA));

        var dialog = _ctx.RenderComponent<RunTrainDialog>(p =>
            p.Add(d => d.Registration, registration)
        );

        var warning = dialog.Find("[data-testid='run-subject-bypass-warning']");
        warning.ClassList.Should().Contain("rz-alert");
        warning
            .TextContent.Should()
            .Contain("subject serialization")
            .And.Contain("concurrently")
            .And.Contain("Queue");
    }

    private sealed class UnusedJobSubmitter : IJobSubmitter
    {
        public Task<string> EnqueueAsync(long metadataId) =>
            throw new InvalidOperationException("Rendering the dialog must not submit a job.");

        public Task<string> EnqueueAsync(long metadataId, object input) =>
            throw new InvalidOperationException("Rendering the dialog must not submit a job.");
    }
}
