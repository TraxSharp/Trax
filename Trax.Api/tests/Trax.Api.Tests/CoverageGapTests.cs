using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using Trax.Api.DTOs;
using Trax.Api.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Enums;

namespace Trax.Api.Tests;

[TestFixture]
public class CoverageGapTests
{
    #region DTOs — simple record construction and equality

    [Test]
    public void DeadLetterSummary_Constructs_AndCarriesValues()
    {
        var now = DateTime.UtcNow;
        var dto = new DeadLetterSummary(
            Id: 1,
            ManifestId: 2,
            ManifestName: "m",
            Status: DeadLetterStatus.AwaitingIntervention,
            DeadLetteredAt: now,
            Reason: "boom",
            RetryCountAtDeadLetter: 3,
            ResolvedAt: now,
            ResolutionNote: "note",
            RetryMetadataId: 99
        );

        dto.Id.Should().Be(1);
        dto.ManifestId.Should().Be(2);
        dto.ManifestName.Should().Be("m");
        dto.Status.Should().Be(DeadLetterStatus.AwaitingIntervention);
        dto.DeadLetteredAt.Should().Be(now);
        dto.Reason.Should().Be("boom");
        dto.RetryCountAtDeadLetter.Should().Be(3);
        dto.ResolvedAt.Should().Be(now);
        dto.ResolutionNote.Should().Be("note");
        dto.RetryMetadataId.Should().Be(99);
        dto.ToString().Should().NotBeNullOrEmpty();
    }

    [Test]
    public void ExecutionSummary_Constructs_AndCarriesValues()
    {
        var dto = new ExecutionSummary(
            Id: 7,
            ExternalId: "ext-7",
            Name: "n",
            TrainState: TrainState.InProgress,
            StartTime: DateTime.UtcNow,
            EndTime: null,
            FailureJunction: null,
            FailureReason: null,
            ManifestId: 42,
            CancellationRequested: true,
            HostName: "host",
            HostEnvironment: "env",
            HostInstanceId: "iid"
        );

        dto.Id.Should().Be(7);
        dto.ExternalId.Should().Be("ext-7");
        dto.Name.Should().Be("n");
        dto.TrainState.Should().Be(TrainState.InProgress);
        dto.StartTime.Should().BeAfter(default);
        dto.EndTime.Should().BeNull();
        dto.FailureJunction.Should().BeNull();
        dto.FailureReason.Should().BeNull();
        dto.ManifestId.Should().Be(42);
        dto.HostName.Should().Be("host");
        dto.HostEnvironment.Should().Be("env");
        dto.HostInstanceId.Should().Be("iid");
        dto.CancellationRequested.Should().BeTrue();
        dto.ToString().Should().NotBeNullOrEmpty();
    }

    [Test]
    public void ManifestGroupSummary_Constructs_AndCarriesValues()
    {
        var dto = new ManifestGroupSummary(
            Id: 1,
            Name: "g",
            MaxActiveJobs: 4,
            Priority: 1,
            IsEnabled: true,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow
        );

        dto.Id.Should().Be(1);
        dto.Name.Should().Be("g");
        dto.MaxActiveJobs.Should().Be(4);
        dto.Priority.Should().Be(1);
        dto.IsEnabled.Should().BeTrue();
        dto.CreatedAt.Should().BeAfter(default);
        dto.UpdatedAt.Should().BeAfter(default);
        dto.ToString().Should().NotBeNullOrEmpty();
    }

    [Test]
    public void ManifestSummary_Constructs_AndCarriesValues()
    {
        var dto = new ManifestSummary(
            Id: 1,
            ExternalId: "ext",
            Name: "m",
            IsEnabled: true,
            ScheduleType: ScheduleType.Cron,
            CronExpression: "* * * * *",
            IntervalSeconds: null,
            MaxRetries: 3,
            TimeoutSeconds: 60,
            LastSuccessfulRun: DateTime.UtcNow,
            ManifestGroupId: 5,
            DependsOnManifestId: null,
            Priority: 0
        );

        dto.Id.Should().Be(1);
        dto.ExternalId.Should().Be("ext");
        dto.Name.Should().Be("m");
        dto.IsEnabled.Should().BeTrue();
        dto.ScheduleType.Should().Be(ScheduleType.Cron);
        dto.CronExpression.Should().Be("* * * * *");
        dto.IntervalSeconds.Should().BeNull();
        dto.MaxRetries.Should().Be(3);
        dto.TimeoutSeconds.Should().Be(60);
        dto.LastSuccessfulRun.Should().NotBeNull();
        dto.ManifestGroupId.Should().Be(5);
        dto.DependsOnManifestId.Should().BeNull();
        dto.Priority.Should().Be(0);
        dto.ToString().Should().NotBeNullOrEmpty();
    }

    [Test]
    public void TrainInfo_Constructs_AndCarriesValues()
    {
        var dto = new TrainInfo(
            ServiceTypeName: "IFoo",
            ImplementationTypeName: "Foo",
            InputTypeName: "FooIn",
            OutputTypeName: "FooOut",
            Lifetime: "Scoped",
            InputSchema: Array.Empty<InputPropertySchema>(),
            RequiredPolicies: Array.Empty<string>(),
            RequiredRoles: Array.Empty<string>(),
            IsQuery: true,
            IsMutation: false,
            GraphQLName: "fooQuery",
            IsBroadcastEnabled: true
        );

        dto.ServiceTypeName.Should().Be("IFoo");
        dto.ImplementationTypeName.Should().Be("Foo");
        dto.InputTypeName.Should().Be("FooIn");
        dto.OutputTypeName.Should().Be("FooOut");
        dto.Lifetime.Should().Be("Scoped");
        dto.InputSchema.Should().BeEmpty();
        dto.RequiredPolicies.Should().BeEmpty();
        dto.RequiredRoles.Should().BeEmpty();
        dto.IsQuery.Should().BeTrue();
        dto.IsMutation.Should().BeFalse();
        dto.GraphQLName.Should().Be("fooQuery");
        dto.IsBroadcastEnabled.Should().BeTrue();
        dto.ToString().Should().NotBeNullOrEmpty();
    }

    [Test]
    public void QueueTrainRequest_Constructs_AndDefaultsPriority()
    {
        var input = JsonDocument.Parse("{\"x\": 1}").RootElement;
        var dto = new QueueTrainRequest("MyApp.IMyTrain", input);

        dto.TrainName.Should().Be("MyApp.IMyTrain");
        dto.Priority.Should().BeNull();

        var withPriority = new QueueTrainRequest("MyApp.IMyTrain", input, Priority: 10);
        withPriority.Priority.Should().Be(10);
    }

    [Test]
    public void RunTrainRequest_Constructs()
    {
        var input = JsonDocument.Parse("{}").RootElement;
        var dto = new RunTrainRequest("IT", input);
        dto.TrainName.Should().Be("IT");
    }

    [Test]
    public void RunTrainResponse_Constructs()
    {
        var dto = new RunTrainResponse(123);
        dto.MetadataId.Should().Be(123);
    }

    [Test]
    public void ScheduleOnceRequest_Constructs()
    {
        var input = JsonDocument.Parse("{}").RootElement;
        var dto = new ScheduleOnceRequest("IT", input, TimeSpan.FromSeconds(30));
        dto.Delay.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void ScheduleOnceResponse_Constructs()
    {
        var dto = new ScheduleOnceResponse(7, "ext");
        dto.ManifestId.Should().Be(7);
        dto.ExternalId.Should().Be("ext");
    }

    [Test]
    public void TriggerDelayedRequest_Constructs()
    {
        var dto = new TriggerDelayedRequest(TimeSpan.FromMinutes(5));
        dto.Delay.Should().Be(TimeSpan.FromMinutes(5));
    }

    #endregion

    #region TraxHealthCheck

    [Test]
    public async Task TraxHealthCheck_HealthyStatus_ReturnsHealthy()
    {
        var svc = Substitute.For<ITraxHealthService>();
        svc.GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(
                new Trax.Api.DTOs.HealthStatus(
                    Status: "Healthy",
                    Description: "ok",
                    QueueDepth: 1,
                    InProgress: 2,
                    FailedLastHour: 0,
                    DeadLetters: 0
                )
            );

        var check = new TraxHealthCheck(svc);
        var ctx = new HealthCheckContext();

        var result = await check.CheckHealthAsync(ctx);

        result
            .Status.Should()
            .Be(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy);
        result.Description.Should().Be("ok");
        result.Data.Should().ContainKey("queueDepth").WhoseValue.Should().Be(1L);
    }

    [Test]
    public async Task TraxHealthCheck_DegradedStatus_ReturnsDegraded()
    {
        var svc = Substitute.For<ITraxHealthService>();
        svc.GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(
                new Trax.Api.DTOs.HealthStatus(
                    Status: "Degraded",
                    Description: "slow",
                    QueueDepth: 100,
                    InProgress: 5,
                    FailedLastHour: 3,
                    DeadLetters: 1
                )
            );

        var check = new TraxHealthCheck(svc);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result
            .Status.Should()
            .Be(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded);
        result.Description.Should().Be("slow");
    }

    [Test]
    public void AddTraxHealthCheck_Registers_HealthCheck()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        var hc = services.AddHealthChecks();

        var result = hc.AddTraxHealthCheck();

        result.Should().BeSameAs(hc);
        var provider = services.BuildServiceProvider();
        var registry =
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        registry.Value.Registrations.Should().Contain(r => r.Name == "trax");
    }

    [Test]
    public void AddTraxHealthCheck_CustomName_UsesProvidedName()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        services.AddHealthChecks().AddTraxHealthCheck("custom-name", "tag1");

        var provider = services.BuildServiceProvider();
        var registry =
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        var reg = registry.Value.Registrations.SingleOrDefault(r => r.Name == "custom-name");
        reg.Should().NotBeNull();
        reg!.Tags.Should().Contain("tag1");
    }

    #endregion

    #region HttpContextPrincipalProvider

    [Test]
    public void HttpContextPrincipalProvider_NoHttpContext_ReturnsNull()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);

        var provider = CreateProvider(accessor);

        provider.GetCurrentPrincipalId().Should().BeNull();
    }

    [Test]
    public void HttpContextPrincipalProvider_Unauthenticated_ReturnsNull()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity()); // no auth type → unauthenticated
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);

        var provider = CreateProvider(accessor);

        provider.GetCurrentPrincipalId().Should().BeNull();
    }

    [Test]
    public void HttpContextPrincipalProvider_AuthenticatedNoClaim_ReturnsNull()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(
            new ClaimsIdentity(claims: null, authenticationType: "test")
        );
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);

        var provider = CreateProvider(accessor);

        provider.GetCurrentPrincipalId().Should().BeNull();
    }

    [Test]
    public void HttpContextPrincipalProvider_AuthenticatedWithClaim_ReturnsValue()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[] { new Claim("trax:principal-id", "user-42") },
                authenticationType: "test"
            )
        );
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);

        var provider = CreateProvider(accessor);

        provider.GetCurrentPrincipalId().Should().Be("user-42");
    }

    private static Trax.Mediator.Services.Principal.ICurrentPrincipalProvider CreateProvider(
        IHttpContextAccessor accessor
    )
    {
        // HttpContextPrincipalProvider is internal; instantiate via reflection.
        var providerType = typeof(Trax.Api.Extensions.ApiServiceExtensions).Assembly.GetType(
            "Trax.Api.Services.Principal.HttpContextPrincipalProvider"
        );
        providerType.Should().NotBeNull();
        return (Trax.Mediator.Services.Principal.ICurrentPrincipalProvider)
            Activator.CreateInstance(providerType!, accessor)!;
    }

    #endregion

    #region LifecycleSubscriptions — passthrough event handlers

    [Test]
    public void LifecycleSubscriptions_AllHandlers_PassthroughEvent()
    {
        var subscription = new Trax.Api.GraphQL.Subscriptions.LifecycleSubscriptions();
        var evt = new TrainLifecycleEvent(
            MetadataId: 1,
            ExternalId: "ext",
            TrainName: "T",
            TrainState: TrainState.InProgress,
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            Output: null
        );

        subscription.OnTrainStarted(evt).Should().BeSameAs(evt);
        subscription.OnTrainCompleted(evt).Should().BeSameAs(evt);
        subscription.OnTrainFailed(evt).Should().BeSameAs(evt);
        subscription.OnTrainCancelled(evt).Should().BeSameAs(evt);
        subscription.OnTrainStateChanged(evt).Should().BeSameAs(evt);
    }

    #endregion

    #region DeadLetterMutations — passthrough to scheduler

    [Test]
    public async Task DeadLetterMutations_AllMethods_DelegateToScheduler()
    {
        var scheduler = Substitute.For<Trax.Scheduler.Services.TraxScheduler.ITraxScheduler>();
        var single = new Trax.Scheduler.Services.TraxScheduler.DeadLetterOperationResult(
            true,
            42L,
            "ok"
        );
        var batch = new Trax.Scheduler.Services.TraxScheduler.BatchDeadLetterResult(1, "ok");

        scheduler.RequeueDeadLetterAsync(7, Arg.Any<CancellationToken>()).Returns(single);
        scheduler
            .AcknowledgeDeadLetterAsync(7, "note", Arg.Any<CancellationToken>())
            .Returns(single);
        scheduler
            .RequeueDeadLettersAsync(Arg.Any<long[]>(), Arg.Any<CancellationToken>())
            .Returns(batch);
        scheduler
            .AcknowledgeDeadLettersAsync(Arg.Any<long[]>(), "note", Arg.Any<CancellationToken>())
            .Returns(batch);
        scheduler.RequeueAllDeadLettersAsync(Arg.Any<CancellationToken>()).Returns(batch);
        scheduler
            .AcknowledgeAllDeadLettersAsync("note", Arg.Any<CancellationToken>())
            .Returns(batch);

        var mutations = new Trax.Api.GraphQL.Mutations.DeadLetterMutations();
        var ct = CancellationToken.None;

        (await mutations.RequeueDeadLetter(7, scheduler, ct)).Should().BeSameAs(single);
        (await mutations.AcknowledgeDeadLetter(7, "note", scheduler, ct)).Should().BeSameAs(single);
        (await mutations.RequeueDeadLetters([1, 2], scheduler, ct)).Should().BeSameAs(batch);
        (await mutations.AcknowledgeDeadLetters([1, 2], "note", scheduler, ct))
            .Should()
            .BeSameAs(batch);
        (await mutations.RequeueAllDeadLetters(scheduler, ct)).Should().BeSameAs(batch);
        (await mutations.AcknowledgeAllDeadLetters("note", scheduler, ct)).Should().BeSameAs(batch);
    }

    #endregion

    #region OperationsMutations — passthrough to scheduler

    [Test]
    public async Task OperationsMutations_AllMethods_DelegateToScheduler()
    {
        var scheduler = Substitute.For<Trax.Scheduler.Services.TraxScheduler.ITraxScheduler>();
        scheduler.CancelAsync("ext", Arg.Any<CancellationToken>()).Returns(3);
        scheduler.TriggerGroupAsync(7L, Arg.Any<CancellationToken>()).Returns(2);
        scheduler.CancelGroupAsync(7L, Arg.Any<CancellationToken>()).Returns(5);

        var mutations = new Trax.Api.GraphQL.Mutations.OperationsMutations();
        var ct = CancellationToken.None;

        mutations.DeadLetters().Should().NotBeNull();
        (await mutations.TriggerManifest("ext", scheduler, ct)).Success.Should().BeTrue();
        (await mutations.TriggerManifestDelayed("ext", TimeSpan.FromSeconds(5), scheduler, ct))
            .Success.Should()
            .BeTrue();
        (await mutations.DisableManifest("ext", scheduler, ct)).Success.Should().BeTrue();
        (await mutations.EnableManifest("ext", scheduler, ct)).Success.Should().BeTrue();
        (await mutations.CancelManifest("ext", scheduler, ct)).Count.Should().Be(3);
        (await mutations.TriggerGroup(7, scheduler, ct)).Count.Should().Be(2);
        (await mutations.CancelGroup(7, scheduler, ct)).Count.Should().Be(5);
    }

    #endregion
}
