using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestSigning;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// <c>UseRemoteWorkers</c> can be called once per endpoint. Each train routed to one of them is
/// sent to that endpoint only, signed with that endpoint's key and carrying only the headers its
/// <c>ConfigureHttpClient</c> added.
/// </summary>
[TestFixture]
public class MultipleRemoteWorkerEndpointsTests
{
    private static readonly byte[] GpuKey = Enumerable.Repeat((byte)1, 32).ToArray();
    private static readonly byte[] CpuKey = Enumerable.Repeat((byte)2, 32).ToArray();

    [Test]
    public async Task Each_routed_train_is_sent_to_its_own_endpoint_with_its_own_credentials()
    {
        var runner = new RecordingRunner();
        using var provider = BuildProvider(
            runner,
            scheduler =>
                scheduler
                    .UseRemoteWorkers(
                        remote =>
                        {
                            remote.BaseUrl = "https://gpu-workers.test/trax/execute";
                            remote.SigningKey = GpuKey;
                            remote.ConfigureHttpClient = client =>
                                client.DefaultRequestHeaders.Add(
                                    "Authorization",
                                    "Bearer gpu-token"
                                );
                        },
                        routing => routing.ForTrain<IGpuTrain>()
                    )
                    .UseRemoteWorkers(
                        remote =>
                        {
                            remote.BaseUrl = "https://cpu-workers.test/trax/execute";
                            remote.SigningKey = CpuKey;
                            remote.ConfigureHttpClient = client =>
                                client.DefaultRequestHeaders.Add(
                                    "Authorization",
                                    "Bearer cpu-token"
                                );
                        },
                        routing => routing.ForTrain<ICpuTrain>()
                    )
        );

        var gpu = await Submit(provider, typeof(IGpuTrain), metadataId: 1);
        var cpu = await Submit(provider, typeof(ICpuTrain), metadataId: 2);

        runner.Requests.Should().HaveCount(2);

        var gpuRequest = runner.Requests.Single(r => r.MetadataId == 1);
        gpuRequest.Uri.Host.Should().Be("gpu-workers.test");
        gpuRequest.Authorization.Should().Equal("Bearer gpu-token");
        gpuRequest.SignedWith(GpuKey).Should().BeTrue();

        var cpuRequest = runner.Requests.Single(r => r.MetadataId == 2);
        cpuRequest.Uri.Host.Should().Be("cpu-workers.test");
        cpuRequest.Authorization.Should().Equal("Bearer cpu-token");
        cpuRequest.SignedWith(CpuKey).Should().BeTrue();

        gpu.Should().NotBeSameAs(cpu);
    }

    [Test]
    public void A_train_routed_to_two_endpoints_is_refused()
    {
        var act = () =>
            BuildProvider(
                new RecordingRunner(),
                scheduler =>
                    scheduler
                        .UseRemoteWorkers(
                            remote => remote.BaseUrl = "https://gpu-workers.test/trax/execute",
                            routing => routing.ForTrain<IGpuTrain>()
                        )
                        .UseRemoteWorkers(
                            remote => remote.BaseUrl = "https://cpu-workers.test/trax/execute",
                            routing => routing.ForTrain<IGpuTrain>()
                        )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*routed to multiple submitters*gpu-workers.test*cpu-workers.test*");
    }

    private static async Task<IJobSubmitter> Submit(
        ServiceProvider provider,
        Type train,
        long metadataId
    )
    {
        using var scope = provider.CreateScope();
        var routing = scope.ServiceProvider.GetRequiredService<JobSubmitterRoutingConfiguration>();
        var submitter = routing.ResolveSubmitter(scope.ServiceProvider, train.FullName!);
        submitter.Should().NotBeNull($"{train.Name} is routed");
        await submitter!.EnqueueAsync(metadataId, CancellationToken.None);
        return submitter;
    }

    private static ServiceProvider BuildProvider(
        RecordingRunner runner,
        Func<SchedulerConfigurationBuilder, SchedulerConfigurationBuilder> configure
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(http =>
            http.ConfigurePrimaryHttpMessageHandler(() => runner)
        );
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(configure)
        );
        return services.BuildServiceProvider();
    }

    private interface IGpuTrain;

    private interface ICpuTrain;

    private sealed record RecordedRequest(
        Uri Uri,
        long MetadataId,
        string[] Authorization,
        byte[] Body,
        string? Signature
    )
    {
        public bool SignedWith(byte[] key) =>
            RunnerRequestSignature.TryVerifyMac(
                key,
                RunnerRequestPurpose.Execute,
                Body,
                Signature,
                out _,
                out _
            );
    }

    /// <summary>Records each request and answers as a runner that accepted it.</summary>
    private sealed class RecordingRunner : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            var job = JsonSerializer.Deserialize<RemoteJobRequest>(
                body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            )!;
            lock (Requests)
                Requests.Add(
                    new RecordedRequest(
                        request.RequestUri!,
                        job.MetadataId,
                        request.Headers.TryGetValues("Authorization", out var values)
                            ? values.ToArray()
                            : [],
                        body,
                        request.Headers.TryGetValues(RunnerRequestSignature.HeaderName, out var sig)
                            ? sig.Single()
                            : null
                    )
                );

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new RemoteJobResponse(job.MetadataId)),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }
}
