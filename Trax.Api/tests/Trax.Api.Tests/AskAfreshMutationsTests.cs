using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Tests.AuthE2E;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.RecordedDecision;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.DeadLetterRequeue;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Api.Tests;

/// <summary>
/// The trigger and dead-letter requeue mutations take <c>askAfresh</c>, and
/// <c>setManifestsReplayDecisionsOnRetry</c> sets whether a manifest's retries replay decisions,
/// through the same scheduler and operations-service calls the dashboard makes. Against Postgres
/// with the real scheduler: a requeue or a released retry keeps its replay link unless asked
/// afresh. Over HTTP: the new argument and mutation answer to the operations gate exactly as the
/// existing mutations do. <c>requeueExecution</c> takes <c>askAfresh</c> too, through
/// <c>IOperationsService.RequeueExecutionAsync</c>, the dashboard's Re-queue path.
/// </summary>
[TestFixture]
[NonParallelizable]
public class AskAfreshMutationsTests
{
    private const string Database = "trax_api_ask_afresh";

    private ServiceProvider _provider = null!;
    private IDataContextProviderFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        AuthE2EHost.EnsureDatabaseExists(Database);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(t =>
            t.AddEffects(e => e.UsePostgres(AuthE2EHost.ConnectionString(Database)))
        );
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IDataContextProviderFactory>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _provider.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    [SetUp]
    public async Task SetUp()
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        await ((DbContext)db).Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE trax.dead_letter, trax.work_queue, trax.metadata, trax.manifest, trax.manifest_group RESTART IDENTITY CASCADE"
        );
    }

    #region Against the real scheduler

    public enum Requeue
    {
        Single,
        Batch,
        All,
    }

    [TestCase(Requeue.Single, false)]
    [TestCase(Requeue.Batch, false)]
    [TestCase(Requeue.All, false)]
    [TestCase(Requeue.Single, true)]
    [TestCase(Requeue.Batch, true)]
    [TestCase(Requeue.All, true)]
    public async Task DeadLetterRequeue_KeepsTheReplayLinkUnlessAskedAfresh(
        Requeue how,
        bool askAfresh
    )
    {
        var (manifest, failedRun) = await SeedFailedManifestAsync();
        var deadLetter = await SeedDeadLetterAsync(manifest);
        var mutations = new DeadLetterMutations();

        switch (how)
        {
            case Requeue.Single:
                (await mutations.RequeueDeadLetter(deadLetter, Scheduler, default, askAfresh))
                    .Success.Should()
                    .BeTrue();
                break;
            case Requeue.Batch:
                (await mutations.RequeueDeadLetters([deadLetter], Scheduler, default, askAfresh))
                    .Count.Should()
                    .Be(1);
                break;
            case Requeue.All:
                var jobs = JobsOver(Scheduler);
                var job = await mutations.RequeueAllDeadLetters(jobs, default, askAfresh);
                await FinishedAsync(jobs, job.Id);
                jobs.Get(job.Id)!.Count.Should().Be(1);
                break;
        }

        var queued = await QueuedEntryAsync(manifest.Id);
        if (askAfresh)
            queued
                .ReplayDecisionsOf.Should()
                .BeNull("the operator asked the requeue to ask afresh");
        else
            queued
                .ReplayDecisionsOf.Should()
                .Be(failedRun, "a dead-letter requeue retries the failed run's decisions");
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Trigger_ReleasingAQueuedRetry_KeepsItsReplayLinkUnlessAskedAfresh(
        bool delayed,
        bool askAfresh
    )
    {
        var (manifest, failedRun) = await SeedFailedManifestAsync();
        await SeedQueuedRetryAsync(manifest, failedRun);
        var mutations = new OperationsMutations();

        var response = delayed
            ? await mutations.TriggerManifestDelayed(
                manifest.ExternalId,
                TimeSpan.FromMinutes(1),
                Scheduler,
                _factory,
                default,
                askAfresh
            )
            : await mutations.TriggerManifest(
                manifest.ExternalId,
                Scheduler,
                _factory,
                default,
                askAfresh
            );

        response.Success.Should().BeTrue();
        var queued = await QueuedEntryAsync(manifest.Id);
        queued.ReplayDecisionsOf.Should().Be(askAfresh ? null : failedRun);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Trigger_AskedAfreshAfterTheRetryWasClaimed_SaysItStillReplays(bool delayed)
    {
        var scheduler = Substitute.For<ITraxScheduler>();
        var claimed = new ManifestTriggerResult(
            WorkQueueId: 9,
            Created: false,
            ScheduledAt: null,
            AlreadyDispatched: true,
            ReplayDecisionsOf: 77
        );
        await SeedManifestAsync("m");
        scheduler.TriggerAsync("m", true, Arg.Any<CancellationToken>()).Returns(claimed);
        scheduler
            .TriggerAsync("m", Arg.Any<TimeSpan>(), true, Arg.Any<CancellationToken>())
            .Returns(claimed);
        var mutations = new OperationsMutations();

        var response = delayed
            ? await mutations.TriggerManifestDelayed(
                "m",
                TimeSpan.FromMinutes(1),
                scheduler,
                _factory,
                default,
                askAfresh: true
            )
            : await mutations.TriggerManifest("m", scheduler, _factory, default, askAfresh: true);

        response.Success.Should().BeTrue();
        response
            .Message.Should()
            .Contain("already claimed")
            .And.Contain("77", "the operator is told which run's decisions it replays");
    }

    [Test]
    public async Task Trigger_AskedAfreshBeforeTheRetryWasClaimed_SaysOnlyTriggered()
    {
        await SeedManifestAsync("m");
        var scheduler = Substitute.For<ITraxScheduler>();
        scheduler
            .TriggerAsync("m", true, Arg.Any<CancellationToken>())
            .Returns(new ManifestTriggerResult(9, false, null, false, null));

        var response = await new OperationsMutations().TriggerManifest(
            "m",
            scheduler,
            _factory,
            default,
            askAfresh: true
        );

        response.Message.Should().Be("Manifest triggered");
    }

    [Test]
    public async Task SetManifestsReplayDecisionsOnRetry_RoundTripsAndClearsQueuedLinks()
    {
        var (manifest, failedRun) = await SeedFailedManifestAsync();
        await SeedQueuedRetryAsync(manifest, failedRun);
        var mutations = new OperationsMutations();
        var queries = new OperationsQueries();

        var off = await mutations.SetManifestsReplayDecisionsOnRetry(
            [manifest.Id],
            false,
            Operations,
            default
        );

        off.Success.Should().BeTrue(off.Message);
        off.Count.Should().Be(1);
        (await queries.GetManifest(manifest.Id, _factory, default))!
            .ReplayDecisionsOnRetry.Should()
            .BeFalse();
        (await QueuedEntryAsync(manifest.Id))
            .ReplayDecisionsOf.Should()
            .BeNull("turning replay off makes a retry waiting out its backoff ask afresh");

        var again = await mutations.SetManifestsReplayDecisionsOnRetry(
            [manifest.Id],
            false,
            Operations,
            default
        );
        again.Count.Should().Be(0, "only a manifest whose flag differs is written");

        var on = await mutations.SetManifestsReplayDecisionsOnRetry(
            [manifest.Id],
            true,
            Operations,
            default
        );
        on.Count.Should().Be(1);
        (await queries.GetManifest(manifest.Id, _factory, default))!
            .ReplayDecisionsOnRetry.Should()
            .BeTrue();
    }

    [Test]
    public async Task SetManifestsReplayDecisionsOnRetry_EmptyList_IsARefusal()
    {
        var response = await new OperationsMutations().SetManifestsReplayDecisionsOnRetry(
            [],
            false,
            Operations,
            default
        );

        response.Success.Should().BeFalse();
    }

    #endregion

    #region A scheduler that predates the overloads

    [Test]
    public async Task WithoutAskAfresh_TheOriginalOverloadsAreCalled()
    {
        var scheduler = Substitute.For<ITraxScheduler>();
        scheduler
            .RequeueDeadLetterAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new DeadLetterOperationResult(true, 1, "ok"));
        scheduler
            .RequeueDeadLettersAsync(Arg.Any<long[]>(), Arg.Any<CancellationToken>())
            .Returns(new BatchDeadLetterResult(1, "ok"));
        scheduler
            .RequeueAllDeadLettersAsync(Arg.Any<CancellationToken>())
            .Returns(new BatchDeadLetterResult(1, "ok"));

        await SeedManifestAsync("m");
        await new OperationsMutations().TriggerManifest("m", scheduler, _factory, default);
        await new OperationsMutations().TriggerManifestDelayed(
            "m",
            TimeSpan.FromMinutes(1),
            scheduler,
            _factory,
            default
        );
        await new DeadLetterMutations().RequeueDeadLetter(1, scheduler, default);
        await new DeadLetterMutations().RequeueDeadLetters([1], scheduler, default);
        var jobs = JobsOver(scheduler);
        var job = await new DeadLetterMutations().RequeueAllDeadLetters(jobs, default);
        await FinishedAsync(jobs, job.Id);

        await scheduler.Received(1).TriggerAsync("m", Arg.Any<CancellationToken>());
        await scheduler
            .DidNotReceive()
            .TriggerAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await scheduler
            .DidNotReceive()
            .TriggerAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
        await scheduler
            .DidNotReceive()
            .RequeueDeadLetterAsync(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await scheduler
            .DidNotReceive()
            .RequeueDeadLettersAsync(
                Arg.Any<long[]>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
        await scheduler
            .DidNotReceive()
            .RequeueAllDeadLettersAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RequeueAll_InTheOtherModeWhileOneRuns_IsNotStarted()
    {
        var scheduler = Substitute.For<ITraxScheduler>();
        var fold = new TaskCompletionSource<BatchDeadLetterResult>();
        scheduler
            .RequeueAllDeadLettersAsync(
                Arg.Any<bool>(),
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(fold.Task);
        var jobs = JobsOver(scheduler);
        var mutations = new DeadLetterMutations();

        var replaying = await mutations.RequeueAllDeadLetters(jobs, default);
        var afresh = await mutations.RequeueAllDeadLetters(jobs, default, askAfresh: true);

        replaying.Started.Should().BeTrue();
        afresh.Started.Should().BeFalse("a fold is already running on this node");
        afresh.Id.Should().Be(replaying.Id);
        afresh
            .Message.Should()
            .Be(DeadLetterRequeueJobs.OtherModeMessage(runningAsksAfresh: false));
        await scheduler
            .Received(1)
            .RequeueAllDeadLettersAsync(
                false,
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>()
            );

        fold.SetResult(new BatchDeadLetterResult(0, "done"));
        await FinishedAsync(jobs, replaying.Id);
    }

    #endregion

    #region Over HTTP, behind the operations gate

    private const string AdminKey = "ask-afresh-admin";
    private const string ReaderKey = "ask-afresh-reader";

    private static readonly string[] Documents =
    [
        """mutation { operations { triggerManifest(externalId: "m", askAfresh: true) { success } } }""",
        """mutation { operations { triggerManifestDelayed(externalId: "m", delay: "PT5M", askAfresh: true) { success } } }""",
        """mutation { operations { deadLetters { requeueDeadLetter(id: 1, askAfresh: true) { success } } } }""",
        """mutation { operations { deadLetters { requeueDeadLetters(ids: [1], askAfresh: true) { count } } } }""",
        """mutation { operations { deadLetters { requeueAllDeadLetters(askAfresh: true) { id } } } }""",
        """mutation { operations { setManifestsReplayDecisionsOnRetry(ids: [1], replay: false) { success count } } }""",
        """mutation { operations { requeueExecution(id: 1, askAfresh: true) { success } } }""",
    ];

    [Test]
    public async Task CallersWithoutTheOperationsRole_AreRefusedAndNothingIsCalled(
        [ValueSource(nameof(Documents))] string document,
        [Values(ReaderKey, null)] string? apiKey
    )
    {
        var (host, scheduler, operations) = await StartHostAsync();
        using var _ = host;

        using var doc = await PostAsync(host, document, apiKey);

        (
            AuthOperations.HasErrorCode(doc, "TRAX_AUTHORIZATION")
            || AuthOperations.HasErrorCode(doc, "AUTH_NOT_AUTHENTICATED")
            || AuthOperations.HasErrorCode(doc, "AUTH_NOT_AUTHORIZED")
        )
            .Should()
            .BeTrue(doc.RootElement.GetRawText());
        scheduler.ReceivedCalls().Should().BeEmpty();
        operations.ReceivedCalls().Should().BeEmpty();
        await host.StopAsync();
    }

    [Test]
    public async Task TheOperationsRole_ReachesTheAskAfreshOverloads()
    {
        var (host, scheduler, operations) = await StartHostAsync();
        using var _ = host;

        foreach (var document in Documents)
        {
            using var doc = await PostAsync(host, document, AdminKey);
            doc.RootElement.TryGetProperty("errors", out var errors)
                .Should()
                .BeFalse(errors.ValueKind == JsonValueKind.Undefined ? "" : errors.GetRawText());
        }

        // Requeue-all answers with a job and runs it in the background.
        await FoldStartedAsync(scheduler);

        await operations
            .Received(1)
            .TriggerManifestAsync("m", null, true, Arg.Any<CancellationToken>());
        await operations
            .Received(1)
            .TriggerManifestAsync("m", TimeSpan.FromMinutes(5), true, Arg.Any<CancellationToken>());
        await scheduler.Received(1).RequeueDeadLetterAsync(1, true, Arg.Any<CancellationToken>());
        await scheduler
            .Received(1)
            .RequeueDeadLettersAsync(
                Arg.Is<long[]>(ids => ids != null && ids.SequenceEqual(new[] { 1L })),
                true,
                Arg.Any<CancellationToken>()
            );
        await scheduler
            .Received(1)
            .RequeueAllDeadLettersAsync(
                true,
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>()
            );
        await operations
            .Received(1)
            .SetManifestsReplayDecisionsOnRetryAsync(
                Arg.Is<IReadOnlyCollection<long>>(ids =>
                    ids != null && ids.SequenceEqual(new[] { 1L })
                ),
                false,
                Arg.Any<CancellationToken>()
            );
        await operations.Received(1).RequeueExecutionAsync(1, true, Arg.Any<CancellationToken>());
        await host.StopAsync();
    }

    private async Task<(
        IHost Host,
        ITraxScheduler Scheduler,
        IOperationsService Operations
    )> StartHostAsync()
    {
        // The triggers refuse an unknown manifest, so the one the documents name exists.
        await SeedManifestAsync("m");

        var scheduler = Substitute.For<ITraxScheduler>();
        scheduler
            .RequeueDeadLetterAsync(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new DeadLetterOperationResult(true, 1, "ok"));
        scheduler
            .RequeueDeadLettersAsync(
                Arg.Any<long[]>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new BatchDeadLetterResult(1, "ok"));
        scheduler
            .RequeueAllDeadLettersAsync(
                Arg.Any<bool>(),
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new BatchDeadLetterResult(1, "ok"));
        scheduler
            .TriggerAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new ManifestTriggerResult(1, true, null, false, null));
        scheduler
            .TriggerAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ManifestTriggerResult(1, true, null, false, null));
        var operations = Substitute.For<IOperationsService>();
        operations
            .RequeueExecutionAsync(Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new OperationResult(true, Count: 1));
        operations
            .TriggerManifestAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new TriggerManifestResult(
                    true,
                    "triggered",
                    new ManifestTriggerResult(1, true, null, false, null)
                )
            );
        operations
            .SetManifestsReplayDecisionsOnRetryAsync(
                Arg.Any<IReadOnlyCollection<long>>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new OperationResult(true, Count: 1));

        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddTraxApiKeyAuth(keys =>
                            keys.Add(AdminKey, id: "admin", "Admin")
                                .Add(ReaderKey, id: "reader", "Reader")
                        );
                        services.AddSingleton<TraxMarker>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([]);
                        services.AddSingleton(discovery);
                        services.AddSingleton(Substitute.For<IEffectRegistry>());
                        services.AddSingleton(_factory);
                        services.AddTraxGraphQL(graphql =>
                            graphql
                                .ExposeOperationQueries()
                                .ExposeOperationMutations()
                                .GateOperations(roles: "Admin")
                        );

                        services.AddScoped(_ => operations);
                        services.AddScoped(_ => scheduler);
                        services.AddScoped(_ => Substitute.For<ITrainExecutionService>());
                        services.AddScoped(_ =>
                            Substitute.For<Trax.Scheduler.Services.JobSubmitter.IJobSubmitter>()
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .Build();

        await host.StartAsync();
        return (host, scheduler, operations);
    }

    private static async Task<JsonDocument> PostAsync(IHost host, string query, string? apiKey)
    {
        var client = host.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query }),
        };
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);

        var response = await client.SendAsync(request);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    #endregion

    #region Seeding

    private ITraxScheduler Scheduler =>
        new TraxScheduler(
            _factory,
            Substitute.For<Trax.Mediator.Services.TrainRegistry.ITrainRegistry>(),
            Substitute.For<ICancellationRegistry>(),
            NullLogger<TraxScheduler>.Instance
        );

    private IOperationsService Operations =>
        new OperationsService(
            Substitute.For<ITrainDiscoveryService>(),
            _factory,
            new SchedulerConfiguration(),
            Substitute.For<ITrainExecutionService>(),
            new ServiceCollection()
                .AddSingleton(Substitute.For<ICancellationRegistry>())
                .BuildServiceProvider()
        );

    /// <summary>
    /// A manifest whose latest run failed after recording a decision, with the work queue entry
    /// it ran from, so a retry of it replays that run.
    /// </summary>
    /// <summary>A requeue-all job runner over <paramref name="scheduler"/>, as the host registers one.</summary>
    private IDeadLetterRequeueJobs JobsOver(ITraxScheduler scheduler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(scheduler);
        services.AddSingleton(_factory);
        return new DeadLetterRequeueJobs(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DeadLetterRequeueJobs>.Instance,
            TimeProvider.System
        );
    }

    /// <summary>Waits for the background requeue-all <paramref name="id"/> to finish.</summary>
    private static async Task FinishedAsync(IDeadLetterRequeueJobs jobs, Guid id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (jobs.Get(id)?.Status == DeadLetterRequeueJobStatus.Running)
            // determinism: the poll interval of a wait bounded by the timeout above.
            await Task.Delay(10, timeout.Token);
    }

    /// <summary>Waits for a background requeue-all to call the scheduler.</summary>
    private static async Task FoldStartedAsync(ITraxScheduler scheduler)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (
            !scheduler
                .ReceivedCalls()
                .Any(c =>
                    c.GetMethodInfo().Name == nameof(ITraxScheduler.RequeueAllDeadLettersAsync)
                )
        )
            // determinism: the poll interval of a wait bounded by the timeout above.
            await Task.Delay(10, timeout.Token);
    }

    /// <summary>A manifest with this external id, so a trigger for it is not refused as unknown.</summary>
    private async Task SeedManifestAsync(string externalId)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var group = new ManifestGroup
        {
            Name = $"group-{Guid.NewGuid():N}",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await db.Track(group);
        await db.SaveChanges(default);

        var manifest = Manifest.Create(new CreateManifest { Name = typeof(IAskAfreshTrain) });
        manifest.ManifestGroupId = group.Id;
        manifest.ExternalId = externalId;
        await db.Track(manifest);
        await db.SaveChanges(default);
    }

    private async Task<(Manifest Manifest, long FailedRun)> SeedFailedManifestAsync()
    {
        await using var db = await _factory.CreateDbContextAsync(default);

        var group = new ManifestGroup
        {
            Name = $"group-{Guid.NewGuid():N}",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await db.Track(group);
        await db.SaveChanges(default);

        var manifest = Manifest.Create(new CreateManifest { Name = typeof(IAskAfreshTrain) });
        manifest.ManifestGroupId = group.Id;
        manifest.PropertyTypeName = "Trax.X.AskAfreshInput";
        manifest.Properties = "{\"v\":1}";
        await db.Track(manifest);
        await db.SaveChanges(default);

        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = manifest.Name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        run.ManifestId = manifest.Id;
        run.TrainState = TrainState.Failed;
        run.DecisionsRecorded = true;
        run.StartTime = DateTime.UtcNow.AddMinutes(-5);
        await db.Track(run);
        await db.SaveChanges(default);

        var ranFrom = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
            }
        );
        ranFrom.Status = WorkQueueStatus.Dispatched;
        ranFrom.MetadataId = run.Id;
        await db.Track(ranFrom);

        db.RecordedDecisions.Add(
            new RecordedDecision
            {
                MetadataId = run.Id,
                QuestionKey = "AskAfreshLane",
                Occurrence = 0,
                Kind = "choice",
                Question = """{"text":"Which lane?"}""",
                Answer = """{"choice":"Fast"}""",
                Fingerprint = "fingerprint",
                DecidedAt = DateTime.UtcNow.AddMinutes(-5),
            }
        );
        await db.SaveChanges(default);
        return (manifest, run.Id);
    }

    private async Task<long> SeedDeadLetterAsync(Manifest manifest)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "failed",
                RetryCount = 0,
            }
        );
        await db.Track(deadLetter);
        await db.SaveChanges(default);
        return deadLetter.Id;
    }

    private async Task SeedQueuedRetryAsync(Manifest manifest, long failedRun)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var retry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
                ScheduledAt = DateTime.UtcNow.AddHours(1),
            }
        );
        retry.ReplayDecisionsOf = failedRun;
        await db.Track(retry);
        await db.SaveChanges(default);
    }

    private async Task<WorkQueue> QueuedEntryAsync(long manifestId)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        return await db
            .WorkQueues.AsNoTracking()
            .SingleAsync(w => w.ManifestId == manifestId && w.Status == WorkQueueStatus.Queued);
    }

    public interface IAskAfreshTrain;

    #endregion
}
