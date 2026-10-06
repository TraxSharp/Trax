using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Services.HealthCheck;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.DeadLetterRequeue;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// <c>requeueAllDeadLetters</c> starts the fold in the background and answers at once with a
/// handle that <c>requeueAllJob</c> reads; the fold is bound to the host's lifetime, never to the
/// request's. Enforces
/// <c>docs/adr/0036-requeue-all-runs-in-the-background-and-returns-a-handle.md</c>.
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0036-requeue-all-runs-in-the-background-and-returns-a-handle.md")]
public class RequeueAllDeadLettersJobTests
{
    private const string Adr =
        "docs/adr/0036-requeue-all-runs-in-the-background-and-returns-a-handle.md";

    private const int Awaiting = 3;

    private ITraxScheduler _scheduler = null!;
    private TaskCompletionSource<BatchDeadLetterResult> _fold = null!;
    private CancellationToken _foldToken;
    private TaskCompletionSource _foldStarted = null!;
    private CancellationTokenSource _stopping = null!;
    private ManualTime _time = null!;
    private ServiceProvider _provider = null!;
    private IRequestExecutor _executor = null!;

    [SetUp]
    public async Task SetUp()
    {
        _fold = new TaskCompletionSource<BatchDeadLetterResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _foldStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _scheduler = Substitute.For<ITraxScheduler>();
        _scheduler
            .RequeueAllDeadLettersAsync(
                Arg.Any<bool>(),
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                _foldToken = call.Arg<CancellationToken>();
                _foldStarted.TrySetResult();
                return _fold.Task.WaitAsync(_foldToken);
            });

        _stopping = new CancellationTokenSource();
        _time = new ManualTime(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

        var factory = new InMemoryContextProviderFactory(new InMemoryDatabaseRoot());
        await using (var db = await factory.CreateDbContextAsync(default))
        {
            var manifest = Manifest.Create(new CreateManifest { Name = typeof(IJobTrain) });
            await db.Track(manifest);
            for (var i = 0; i < Awaiting + 2; i++)
            {
                var dl = DeadLetter.Create(
                    new CreateDeadLetter
                    {
                        Manifest = manifest,
                        Reason = $"failure-{i}",
                        RetryCount = 3,
                    }
                );
                if (i >= Awaiting)
                    dl.Acknowledge("handled");
                await db.Track(dl);
            }
            await db.SaveChanges(default);
        }

        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery
            .DiscoverTrains()
            .Returns([
                new TrainRegistration
                {
                    ServiceType = typeof(IJobTrain),
                    ImplementationType = typeof(JobTrain),
                    InputType = typeof(JobInput),
                    OutputType = typeof(Unit),
                    Lifetime = ServiceLifetime.Scoped,
                    ServiceTypeName = nameof(IJobTrain),
                    ImplementationTypeName = nameof(JobTrain),
                    HasAllowAnonymousAttribute = true,
                    InputTypeName = nameof(JobInput),
                    OutputTypeName = nameof(Unit),
                    RequiredPolicies = [],
                    RequiredRoles = [],
                    IsQuery = false,
                    IsMutation = true,
                    IsRemote = false,
                    IsBroadcastEnabled = false,
                    GraphQLOperations = GraphQLOperation.Run,
                },
            ]);

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(_stopping.Token);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Trax.Effect.Configuration.TraxBuilder.TraxMarker>();
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton<IDataContextProviderFactory>(factory);
        services.AddSingleton(lifetime);
        services.AddSingleton<TimeProvider>(_time);
        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            graphql =>
                graphql
                    .ExposeOperationQueries()
                    .ExposeOperationMutations()
                    .AllowAnonymousOperations()
        );
        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
        services.AddScoped(_ => _scheduler);

        _provider = services.BuildServiceProvider();
        _executor = await _provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
    }

    [TearDown]
    public async Task TearDown()
    {
        _fold.TrySetCanceled();
        await _provider.DisposeAsync();
        _stopping.Dispose();
    }

    private IDeadLetterRequeueJobs Jobs => _provider.GetRequiredService<IDeadLetterRequeueJobs>();

    /// <summary>Waits for the background fold of job <paramref name="id"/> to finish.</summary>
    private async Task FinishedAsync(string id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Jobs.Get(Guid.Parse(id))?.Status == DeadLetterRequeueJobStatus.Running)
            // determinism: the poll interval of a wait bounded by the timeout above.
            await Task.Delay(10, timeout.Token);
    }

    private async Task<JsonElement> ExecuteAsync(string query, CancellationToken ct = default)
    {
        var result = (OperationResult)await _executor.ExecuteAsync(query, ct);
        var json = result.ToJson();
        result.Errors.Should().BeNullOrEmpty(json);
        return JsonDocument.Parse(json).RootElement.GetProperty("data");
    }

    private Task<JsonElement> StartAsync(CancellationToken ct = default) =>
        ExecuteAsync(
                """
                mutation {
                  operations {
                    deadLetters {
                      requeueAllDeadLetters {
                        id status awaitingAtStart startedAt finishedAt count message started
                      }
                    }
                  }
                }
                """,
                ct
            )
            .ContinueWith(t =>
                t.Result.GetProperty("operations")
                    .GetProperty("deadLetters")
                    .GetProperty("requeueAllDeadLetters")
            );

    private async Task<JsonElement> ReadAsync(string id)
    {
        var data = await ExecuteAsync(
            "{ operations { deadLetters { requeueAllJob(id: \""
                + id
                + "\") { id status awaitingAtStart finishedAt count message } } } }"
        );
        return data.GetProperty("operations")
            .GetProperty("deadLetters")
            .GetProperty("requeueAllJob");
    }

    [Test]
    public async Task Start_ReturnsAHandleWhileTheFoldIsStillRunning()
    {
        var job = await StartAsync();

        job.GetProperty("status").GetString().Should().Be("RUNNING", Because("returns at once"));
        job.GetProperty("awaitingAtStart").GetInt32().Should().Be(Awaiting);
        job.GetProperty("count").ValueKind.Should().Be(JsonValueKind.Null);
        job.GetProperty("finishedAt").ValueKind.Should().Be(JsonValueKind.Null);
        job.GetProperty("started").GetBoolean().Should().BeTrue();

        var read = await ReadAsync(job.GetProperty("id").GetString()!);
        read.GetProperty("status").GetString().Should().Be("RUNNING");
    }

    [Test]
    public async Task Fold_WhenItFinishes_IsReadAsSucceededWithItsResult()
    {
        var id = (await StartAsync()).GetProperty("id").GetString()!;

        _fold.SetResult(new BatchDeadLetterResult(Awaiting, "3 dead letter(s) requeued."));
        await FinishedAsync(id);

        var read = await ReadAsync(id);
        read.GetProperty("status").GetString().Should().Be("SUCCEEDED");
        read.GetProperty("count").GetInt32().Should().Be(Awaiting);
        read.GetProperty("message").GetString().Should().Be("3 dead letter(s) requeued.");
        read.GetProperty("finishedAt").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Test]
    public async Task Fold_IsNotBoundToTheRequestThatStartedIt()
    {
        using var request = new CancellationTokenSource();
        var job = await StartAsync(request.Token);

        await request.CancelAsync();
        await _foldStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        _foldToken.Should().Be(_stopping.Token, Because("binds the fold to the host's lifetime"));
        _foldToken.IsCancellationRequested.Should().BeFalse();
        Jobs.Get(Guid.Parse(job.GetProperty("id").GetString()!))!
            .Status.Should()
            .Be(DeadLetterRequeueJobStatus.Running, "the fold runs on after the request ends");
    }

    [Test]
    public async Task Fold_WhenTheHostStops_IsReadAsCanceled()
    {
        var id = (await StartAsync()).GetProperty("id").GetString()!;

        await _stopping.CancelAsync();
        await FinishedAsync(id);

        var read = await ReadAsync(id);
        read.GetProperty("status").GetString().Should().Be("CANCELED");
        read.GetProperty("message").GetString().Should().Be(DeadLetterRequeueJobs.CanceledMessage);
    }

    [Test]
    public async Task Fold_WhenTheServerFails_IsReadAsFailedWithNothingAboutTheServer()
    {
        var id = (await StartAsync()).GetProperty("id").GetString()!;

        _fold.SetException(new InvalidOperationException("connection to 10.0.0.5 refused"));
        await FinishedAsync(id);

        var read = await ReadAsync(id);
        read.GetProperty("status").GetString().Should().Be("FAILED");
        read.GetProperty("message").GetString().Should().Be(DeadLetterRequeueJobs.FailedMessage);
        read.GetRawText().Should().NotContain("10.0.0.5");
    }

    [Test]
    public async Task Start_WhileOneIsRunning_ReturnsThatOneAndStartsNoOther()
    {
        var first = await StartAsync();
        var second = await StartAsync();

        second.GetProperty("id").GetString().Should().Be(first.GetProperty("id").GetString());
        second.GetProperty("started").GetBoolean().Should().BeFalse();
        await _scheduler
            .Received(1)
            .RequeueAllDeadLettersAsync(
                Arg.Any<bool>(),
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task Start_AfterTheLastOneFinished_StartsANewOne()
    {
        var first = (await StartAsync()).GetProperty("id").GetString()!;
        _fold.SetResult(new BatchDeadLetterResult(Awaiting, "done"));
        await FinishedAsync(first);

        var second = await StartAsync();

        second.GetProperty("id").GetString().Should().NotBe(first);
        second.GetProperty("started").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task RequeueAllJob_UnknownId_IsNull()
    {
        var data = await ExecuteAsync(
            "{ operations { deadLetters { requeueAllJob(id: \""
                + Guid.NewGuid()
                + "\") { id } } } }"
        );

        data.GetProperty("operations")
            .GetProperty("deadLetters")
            .GetProperty("requeueAllJob")
            .ValueKind.Should()
            .Be(JsonValueKind.Null);
    }

    [Test]
    public async Task FinishedJob_IsForgottenAfterTheRetention()
    {
        var id = (await StartAsync()).GetProperty("id").GetString()!;
        _fold.SetResult(new BatchDeadLetterResult(Awaiting, "done"));
        await FinishedAsync(id);

        _time.Advance(DeadLetterRequeueJobs.Retention - TimeSpan.FromMinutes(1));
        Jobs.Get(Guid.Parse(id)).Should().NotBeNull(Because("keeps a finished job for 24 hours"));

        _time.Advance(TimeSpan.FromMinutes(2));
        Jobs.Get(Guid.Parse(id)).Should().BeNull(Because("forgets a finished job after 24 hours"));
    }

    [Test]
    public async Task FinishedJobs_AreKeptUpToTheLimit_OldestForgottenFirst()
    {
        _scheduler
            .RequeueAllDeadLettersAsync(
                Arg.Any<bool>(),
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new BatchDeadLetterResult(0, "nothing to requeue"));
        var jobs = Jobs;

        var ids = new List<Guid>();
        for (var i = 0; i <= DeadLetterRequeueJobs.MaxRetained; i++)
        {
            ids.Add((await jobs.StartAsync()).Id);
            await FinishedAsync(ids[^1].ToString());
            _time.Advance(TimeSpan.FromSeconds(1));
        }
        await FinishedAsync((await jobs.StartAsync()).Id.ToString());

        jobs.Get(ids[0]).Should().BeNull(Because("keeps at most 100 finished jobs"));
        jobs.Get(ids[^1]).Should().NotBeNull();
    }

    private static string Because(string rule) => $"{rule} ({Adr})";

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private interface IJobTrain;

    private class JobTrain;

    public record JobInput
    {
        public string Value { get; init; } = "";
    }
}
