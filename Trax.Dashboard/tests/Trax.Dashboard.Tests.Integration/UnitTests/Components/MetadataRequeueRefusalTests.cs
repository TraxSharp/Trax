using System.Reflection;
using AwesomeAssertions;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Core.Functional;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// Re-queueing a run reads its saved input back as the train's input. When what was saved is not
/// that input (a placeholder the parameter effect wrote in its place, or an input with
/// <c>[TraxSensitive]</c> members masked), it would read back as defaults and the train would run
/// with values it never had, so the page refuses and says why, as the API's
/// <c>requeueExecution</c> does. An input that merely has a member named like a marker is a real
/// input and is queued.
/// </summary>
[TestFixture]
public class MetadataRequeueRefusalTests
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

        var trains = new ServiceCollection();
        trains.AddScopedTraxRoute<IRequeuedTrain, RequeuedTrain>();
        var discovery = new TrainDiscoveryService(trains);

        var services = _ctx.Services;
        services.AddSingleton<IDataContextProviderFactory>(_data);
        services.AddSingleton<ILocalStorageService, InMemoryLocalStorageService>();
        services.AddSingleton<IDashboardSettingsService, DashboardSettingsService>();
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
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [TestCase(
        """{"_truncated": true, "_maxBytes": 1024}""",
        "was too large to save in full",
        TestName = "A_truncated_input_is_not_requeued"
    )]
    [TestCase(
        """{"_unserializable": true, "_error": "NotSupportedException"}""",
        "recorded as a _unserializable placeholder",
        TestName = "An_unserializable_input_is_not_requeued"
    )]
    [TestCase(
        """{"_disposed": true, "_message": "Input object contained disposed JsonDocument objects"}""",
        "recorded as a _disposed placeholder",
        TestName = "A_disposed_input_is_not_requeued"
    )]
    [TestCase(
        """{"Value": "kept", "Token": {"_redacted": true}}""",
        "masked by [TraxSensitive]",
        TestName = "An_input_with_a_masked_member_is_not_requeued"
    )]
    public async Task A_saved_input_that_is_not_the_input_it_ran_with_is_refused(
        string savedInput,
        string reason
    )
    {
        var metadataId = await SeedRunAsync(savedInput);

        var page = _ctx.RenderComponent<MetadataDetailPage>(p =>
            p.Add(x => x.MetadataId, metadataId)
        );
        var requeue = page.WaitForElement("button:contains('Re-queue')", TimeSpan.FromSeconds(10));
        await requeue.ClickAsync(new());

        page.WaitForAssertion(
            () =>
                page.FindAll(".rz-alert")
                    .Select(a => a.TextContent)
                    .Should()
                    .Contain(t =>
                        t.Contains(reason)
                        && t.Contains("cannot be re-queued with what it ran with")
                    ),
            TimeSpan.FromSeconds(10)
        );
        (await QueuedCountAsync())
            .Should()
            .Be(0, "the train would run with defaults in place of the values it ran with");
    }

    [Test]
    public async Task A_run_whose_train_is_no_longer_registered_says_so()
    {
        var metadataId = await SeedRunAsync("""{"Value": "kept"}""", "Some.Retired.ITrain");

        await ClickRequeueAndExpectAsync(
            metadataId,
            $"Train Some.Retired.ITrain is no longer registered, so execution {metadataId} "
                + "cannot be re-queued."
        );
        (await QueuedCountAsync()).Should().Be(0);
    }

    [Test]
    public async Task A_saved_input_that_no_longer_reads_as_its_type_is_refused_as_the_runs_input()
    {
        // Saved when Value was an object; the input type now says it is a string.
        var metadataId = await SeedRunAsync("""{"Value": {"was": "an object"}}""");

        await ClickRequeueAndExpectAsync(
            metadataId,
            $"The saved input of run {metadataId} no longer reads as "
                + $"{typeof(RequeuedInput).FullName}: "
        );
        (await QueuedCountAsync()).Should().Be(0);
    }

    private async Task ClickRequeueAndExpectAsync(long metadataId, string message)
    {
        var page = _ctx.RenderComponent<MetadataDetailPage>(p =>
            p.Add(x => x.MetadataId, metadataId)
        );
        var requeue = page.WaitForElement("button:contains('Re-queue')", TimeSpan.FromSeconds(10));
        await requeue.ClickAsync(new());

        page.WaitForAssertion(
            () =>
                page.FindAll(".rz-alert")
                    .Select(a => a.TextContent)
                    .Should()
                    .Contain(t => t.Contains(message) && !t.Contains("InputJson")),
            TimeSpan.FromSeconds(10)
        );
    }

    [Test]
    public async Task An_input_with_a_member_named_like_a_marker_is_requeued()
    {
        var metadataId = await SeedRunAsync("""{"Value": "kept", "_truncated": false}""");

        var page = _ctx.RenderComponent<MetadataDetailPage>(p =>
            p.Add(x => x.MetadataId, metadataId)
        );
        var requeue = page.WaitForElement("button:contains('Re-queue')", TimeSpan.FromSeconds(10));
        await requeue.ClickAsync(new());

        var navigation = _ctx.Services.GetRequiredService<FakeNavigationManager>();
        page.WaitForAssertion(
            () => navigation.Uri.Should().Contain("trax/data/work-queue/"),
            TimeSpan.FromSeconds(10)
        );
        (await QueuedCountAsync()).Should().Be(1, "a marker name with any value but true is data");
    }

    [Test]
    public async Task The_requeue_goes_through_the_shared_requeue_and_replays_nothing_when_the_run_decided_nothing()
    {
        var metadataId = await SeedRunAsync("""{"Value": "kept"}""");
        var calls = new List<(string Method, bool Trusted)>();
        _ctx.Services.AddScoped<IOperationsService>(sp =>
            RecordingOperations.Wrap(
                new OperationsService(
                    sp.GetRequiredService<ITrainDiscoveryService>(),
                    _data,
                    new SchedulerConfiguration(),
                    sp.GetRequiredService<ITrainExecutionService>()
                ),
                sp.GetRequiredService<ITrustedExecutionScope>(),
                calls
            )
        );

        var page = _ctx.RenderComponent<MetadataDetailPage>(p =>
            p.Add(x => x.MetadataId, metadataId)
        );
        var requeue = page.WaitForElement("button:contains('Re-queue')", TimeSpan.FromSeconds(10));
        await requeue.ClickAsync(new());

        var navigation = _ctx.Services.GetRequiredService<FakeNavigationManager>();
        page.WaitForAssertion(
            () => navigation.Uri.Should().Contain("trax/data/work-queue/"),
            TimeSpan.FromSeconds(10)
        );
        calls
            .Should()
            .Equal(
                [("RequeueExecutionAsync", true)],
                "the page makes the API's requeueExecution call, inside the dashboard's trusted "
                    + "scope (docs/0017), and nothing else"
            );
        await using var db = await _data.CreateDbContextAsync(default);
        (await db.WorkQueues.AsNoTracking().SingleAsync())
            .ReplayDecisionsOf.Should()
            .BeNull("there is nothing to replay, so it is queued as an ordinary enqueue");
    }

    /// <summary>
    /// Forwards every call to the real operations service and records which method was called and
    /// whether the caller was inside a trusted scope at the time.
    /// </summary>
    public class RecordingOperations : DispatchProxy
    {
        private IOperationsService _inner = null!;
        private ITrustedExecutionScope _scope = null!;
        private List<(string, bool)> _calls = null!;

        public static IOperationsService Wrap(
            IOperationsService inner,
            ITrustedExecutionScope scope,
            List<(string, bool)> calls
        )
        {
            var proxy = Create<IOperationsService, RecordingOperations>();
            var recording = (RecordingOperations)(object)proxy;
            recording._inner = inner;
            recording._scope = scope;
            recording._calls = calls;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            _calls.Add((targetMethod!.Name, _scope.IsTrusted));
            return targetMethod.Invoke(_inner, args);
        }
    }

    private async Task<long> SeedRunAsync(string input, string? trainName = null)
    {
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = trainName ?? typeof(IRequeuedTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        run.Input = input;

        await using var db = await _data.CreateDbContextAsync(default);
        await db.Track(run);
        await db.SaveChanges(default);
        return run.Id;
    }

    private async Task<int> QueuedCountAsync()
    {
        await using var db = await _data.CreateDbContextAsync(default);
        return await db.WorkQueues.AsNoTracking().CountAsync();
    }

    public record RequeuedInput
    {
        public string Value { get; init; } = "";
    }

    public interface IRequeuedTrain : IServiceTrain<RequeuedInput, Unit> { }

    public class RequeuedTrain : ServiceTrain<RequeuedInput, Unit>, IRequeuedTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}
