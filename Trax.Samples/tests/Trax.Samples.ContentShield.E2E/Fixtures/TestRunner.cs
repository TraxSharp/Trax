using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Samples.ContentShield.Runner;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Samples.ContentShield.E2E.Fixtures;

/// <summary>
/// The sample's runner, the real <see cref="Function"/>, served the way <c>dotnet run</c> serves it:
/// <c>RunLocalAsync</c> on a Kestrel port of its own. The only change is where configuration comes
/// from (the test database, the test broker, Development) and a recorder around the request
/// handler, so a test can tell the runner, not the API, executed a train.
/// </summary>
internal sealed class TestRunner : Function
{
    private readonly IConfiguration _configuration;

    private TestRunner(IConfiguration configuration, string baseUrl)
    {
        _configuration = configuration;
        BaseUrl = baseUrl;
    }

    public string BaseUrl { get; }

    public RequestRecorder Requests { get; } = new();

    public static TestRunner Start(string connectionString)
    {
        var baseUrl = $"http://127.0.0.1:{FreePort()}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:TraxDatabase"] = connectionString,
                    ["ConnectionStrings:RabbitMQ"] = TestRabbitMq.ConnectionString,
                    ["DOTNET_ENVIRONMENT"] = "Development",
                }
            )
            .Build();

        var runner = new TestRunner(configuration, baseUrl);

        // RunLocalAsync returns only when its server stops, which is when the test process ends.
        _ = Task.Run(() =>
            runner.RunLocalAsync([
                $"--Kestrel:Endpoints:Http:Url={baseUrl}",
                "--environment=Development",
            ])
        );
        return runner;
    }

    protected override IServiceProvider BuildServiceProvider()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(_configuration);
        services.AddLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Warning));
        ConfigureServices(services, _configuration);
        services.AddTraxJobRunner(runner => ConfigureRunner(runner, _configuration));

        var handler = services.Last(d => d.ServiceType == typeof(ITraxRequestHandler));
        services.Remove(handler);
        services.Add(
            ServiceDescriptor.Describe(
                typeof(ITraxRequestHandler),
                sp => (object)new RecordingRequestHandler(Inner(sp, handler), Requests),
                handler.Lifetime
            )
        );
        return services.BuildServiceProvider();
    }

    private static ITraxRequestHandler Inner(IServiceProvider sp, ServiceDescriptor descriptor) =>
        (ITraxRequestHandler)(
            descriptor.ImplementationFactory?.Invoke(sp)
            ?? descriptor.ImplementationInstance
            ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!)
        );

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public sealed class RequestRecorder
    {
        private int _executes;
        private int _runs;

        public int Executes => Volatile.Read(ref _executes);

        public int Runs => Volatile.Read(ref _runs);

        internal void Execute() => Interlocked.Increment(ref _executes);

        internal void Run() => Interlocked.Increment(ref _runs);
    }

    private sealed class RecordingRequestHandler(
        ITraxRequestHandler inner,
        RequestRecorder recorder
    ) : ITraxRequestHandler
    {
        public Task<ExecuteJobResult> ExecuteJobAsync(
            RemoteJobRequest request,
            CancellationToken ct = default
        )
        {
            recorder.Execute();
            return inner.ExecuteJobAsync(request, ct);
        }

        public Task<RemoteRunResponse> RunTrainAsync(
            RemoteRunRequest request,
            CancellationToken ct = default
        )
        {
            recorder.Run();
            return inner.RunTrainAsync(request, ct);
        }
    }
}
