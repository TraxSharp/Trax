using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Trax.Api.Dashboard.Extensions;
using Trax.Api.Dashboard.Serving;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Api.Tests.PersistedOperations.Fixtures;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.ClientExport;
using Trax.Effect.Configuration.Deploy;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.Deploy;

/// <summary>
/// A web host started with <c>--trax-export-deploy</c> writes what it needs from its deployment, from the endpoints
/// it mapped and the authentication it registered, without serving; and a startup check that refuses the host
/// refuses the export.
///
/// <para>Guard for <c>Trax.Docs/adr/0060-a-trax-app-is-deployed-from-what-its-host-reports.md</c>.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
[Property("adr", "Trax.Docs/adr/0060-a-trax-app-is-deployed-from-what-its-host-reports.md")]
public class DeployManifestExportTests
{
    private string? _path;

    [SetUp]
    public void NewPath() =>
        _path = Path.Combine(Path.GetTempPath(), $"trax-deploy-{Guid.NewGuid():N}", "deploy.json");

    [TearDown]
    public void RemovePath()
    {
        if (Directory.Exists(Path.GetDirectoryName(_path.NotNull())))
            Directory.Delete(Path.GetDirectoryName(_path.NotNull()).NotNull(), recursive: true);
    }

    [Test]
    public async Task The_host_reports_its_endpoints_its_authentication_and_the_api_role()
    {
        await using var app = Build();

        await app.RunTraxAsync(
            ["--trax-export-deploy", _path.NotNull()],
            cancellationToken: CancellationToken.None
        );

        var manifest = DeployManifest.Read(
            await File.ReadAllTextAsync(_path.NotNull(), cancellationToken: CancellationToken.None)
        );
        manifest.Version.Should().Be(DeployManifest.CurrentVersion);
        manifest.Roles.Should().Equal("api");
        manifest.Endpoints.GraphQL.Should().Be("/trax/graphql");
        manifest.Endpoints.Health.Should().Be("/trax/health");
        manifest.Endpoints.Console.Should().Be("/trax/dashboard");
        manifest
            .Authentication.Select(a => (a.Name, a.Handler))
            .Should()
            .Equal((TestUserHandler.Name, nameof(TestUserHandler)));
    }

    [Test]
    public async Task The_export_serves_nothing()
    {
        await using var app = Build();
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

        await app.RunTraxAsync(
            ["--trax-export-deploy", _path.NotNull()],
            cancellationToken: CancellationToken.None
        );

        lifetime.ApplicationStarted.IsCancellationRequested.Should().BeFalse();
    }

    [Test]
    public async Task A_startup_check_that_refuses_the_host_refuses_the_export()
    {
        await using var app = Build(s => s.AddSingleton<IHostedService, RefusingCheck>());

        var act = async () =>
            await app.RunTraxAsync(
                    ["--trax-export-deploy", _path.NotNull()],
                    cancellationToken: CancellationToken.None
                )
                .ConfigureAwait(false);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(RefusingCheck.Message);
        File.Exists(_path.NotNull()).Should().BeFalse();
    }

    [Test]
    public async Task A_host_whose_persisted_operations_invalidate_over_rabbitmq_reports_the_broker()
    {
        await using var app = Build(
            PersistedOperationStorage,
            g =>
                g.UsePersistedOperations(po =>
                    po.UseRabbitMqInvalidation("amqp://localhost", TestInvalidationKey.Sign)
                )
        );

        await app.RunTraxAsync(["--trax-export-deploy", _path]);

        var manifest = DeployManifest.Read(await File.ReadAllTextAsync(_path));
        manifest.Transports.Should().Equal("rabbitmq");
    }

    [Test]
    public async Task A_host_whose_persisted_operation_store_invalidates_over_rabbitmq_reports_the_broker()
    {
        await using var app = Build(s =>
        {
            PersistedOperationStorage(s);
            s.AddPersistedOperationStore(store =>
                store.UseRabbitMqInvalidation("amqp://localhost", TestInvalidationKey.Sign)
            );
        });

        await app.RunTraxAsync(["--trax-export-deploy", _path]);

        var manifest = DeployManifest.Read(await File.ReadAllTextAsync(_path));
        manifest.Transports.Should().Equal("rabbitmq");
    }

    [Test]
    public async Task A_single_node_persisted_operations_host_reports_no_transport()
    {
        await using var app = Build(
            PersistedOperationStorage,
            g => g.UsePersistedOperations(po => po.SingleNode())
        );

        await app.RunTraxAsync(["--trax-export-deploy", _path]);

        var manifest = DeployManifest.Read(await File.ReadAllTextAsync(_path));
        manifest.Transports.Should().BeEmpty();
    }

    // The store reads through the Trax data context; the export never opens it.
    private static void PersistedOperationStorage(IServiceCollection services) =>
        services.AddSingleton(Substitute.For<IDataContextProviderFactory>());

    private static WebApplication Build(
        Action<IServiceCollection>? more = null,
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder>? graphql = null
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }
        );
        builder.WebHost.UseTestServer();
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);

        var services = builder.Services;
        services.AddSingleton(
            new DashboardAssets(
                new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    ["index.html"] = Encoding.UTF8.GetBytes(
                        "<!doctype html><html><head></head><body></body></html>"
                    ),
                }
            )
        );
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<IOperationsService>());
        services.AddSingleton(Substitute.For<ITrainExecutionService>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        services.AddDbContext<OrderTestDbContext>(o =>
            o.UseInMemoryDatabase("Deploy_" + Guid.NewGuid())
        );
        services.AddTraxGraphQL(g =>
        {
            var api = g.AddDbContext<OrderTestDbContext>()
                .ExposeOperationQueries()
                .AllowAnonymousOperations();
            return graphql is null ? api : graphql(api);
        });
        services
            .AddAuthentication(TestUserHandler.Name)
            .AddScheme<AuthenticationSchemeOptions, TestUserHandler>(
                TestUserHandler.Name,
                configureOptions: null
            );
        services.AddAuthorization(o => o.AddPolicy("Operator", p => p.RequireRole("Operator")));
        services.AddHealthChecks();
        more?.Invoke(services);

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseTraxApiDashboard(d => d.RequirePolicy("Operator"));
        app.UseTraxGraphQL();
        app.MapHealthChecks("/trax/health");
        return app;
    }

    private sealed class RefusingCheck : IHostedService, ITraxStartupCheck
    {
        public const string Message = "This host is refused.";

        public Task StartAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Message);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestUserHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "TestUser";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(
                        new ClaimsPrincipal(new ClaimsIdentity([], Name)),
                        Name
                    )
                )
            );
    }
}
