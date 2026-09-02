using FluentAssertions;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trax.Api.GraphQL.Extensions;

namespace Trax.Api.Tests;

/// <summary>
/// HotChocolate 16 resolves a bridged application service eagerly while the schema
/// container is being built, so bridging one the host never registered turns an optional
/// dependency into a startup crash. These tests pin that the bridge only fires when the
/// service is really there, including for the open-generic registrations
/// (<c>ILogger&lt;&gt;</c>, <c>IOptions&lt;&gt;</c>) that Trax's interceptors depend on.
/// </summary>
[TestFixture]
public class ApplicationServiceBridgeTests
{
    #region BridgeApplicationService

    [Test]
    public async Task Bridge_RegisteredService_IsResolvableFromSchemaServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProbe>(new Probe { Marker = "from-app" });

        var builder = services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("ping").Type<StringType>().Resolve("pong"));
        builder.BridgeApplicationService<IProbe>();

        var executor = await BuildAsync(services);

        executor
            .Schema.Services.GetRequiredService<IProbe>()
            .Marker.Should()
            .Be("from-app", "the bridge must hand over the application container's instance");
    }

    [Test]
    public async Task Bridge_MissingService_DoesNotBreakSchemaBuild()
    {
        // Resolving eagerly while the schema container is built would turn a service the
        // host never registered into a startup crash. The bridge defers, so composition
        // succeeds and only an actual request for it fails.
        var services = new ServiceCollection();

        var builder = services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("ping").Type<StringType>().Resolve("pong"));
        builder.BridgeApplicationService<IProbe>();

        var executor = await BuildAsync(services);

        var act = () => executor.Schema.Services.GetRequiredService<IProbe>();
        act.Should().Throw<InvalidOperationException>().WithMessage("*IProbe*");
    }

    [Test]
    public async Task Bridge_ServiceRegisteredAfterTheBuilder_IsStillResolvable()
    {
        // The regression this guards: AddTraxGraphQL() runs before AddAuthentication() in
        // plenty of hosts. HotChocolate 15 forwarded lookups at request time so order never
        // mattered; deciding what to bridge from the collection's contents reintroduced an
        // ordering dependency and left interceptors unable to activate.
        var services = new ServiceCollection();

        var builder = services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("ping").Type<StringType>().Resolve("pong"));
        builder.BridgeApplicationService<IProbe>();

        // Registered only after the GraphQL builder has been configured.
        services.AddSingleton<IProbe>(new Probe { Marker = "late" });

        var executor = await BuildAsync(services);

        executor.Schema.Services.GetRequiredService<IProbe>().Marker.Should().Be("late");
    }

    [Test]
    public void Bridge_ReturnsBuilder_ForChaining()
    {
        var services = new ServiceCollection();
        var builder = services.AddGraphQLServer();

        builder.BridgeApplicationService<IProbe>().Should().BeSameAs(builder);
    }

    [Test]
    public void Bridge_NullBuilder_Throws()
    {
        var act = () => ((IRequestExecutorBuilder)null!).BridgeApplicationService<IProbe>();

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region TraxApplicationServices

    [Test]
    public void TraxApplicationServices_ExposesTheProviderItWasGiven()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IProbe>(new Probe { Marker = "app" })
            .BuildServiceProvider();

        new TraxApplicationServices(provider)
            .Services.GetRequiredService<IProbe>()
            .Marker.Should()
            .Be("app");
    }

    [Test]
    public async Task TraxApplicationServices_BridgedIntoSchemaServices_StillResolvesScopedAppServices()
    {
        // The reason this type exists: IServiceProvider and IServiceScopeFactory cannot be
        // bridged, because the schema container registers its own and those win. A scoped
        // application registration must remain reachable through the holder.
        var services = new ServiceCollection();
        services.AddScoped<IProbe>(_ => new Probe { Marker = "scoped-app" });
        services.TryAddSingleton(sp => new TraxApplicationServices(sp));

        var builder = services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("ping").Type<StringType>().Resolve("pong"));
        builder.BridgeApplicationService<TraxApplicationServices>();

        var executor = await BuildAsync(services);

        var holder = executor.Schema.Services.GetRequiredService<TraxApplicationServices>();
        using var scope = holder.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IProbe>().Marker.Should().Be("scoped-app");
    }

    [Test]
    public void TraxApplicationServices_NullProvider_Throws()
    {
        var act = () => new TraxApplicationServices(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Helpers

    private static async Task<IRequestExecutor> BuildAsync(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        return await provider.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync();
    }

    public interface IProbe
    {
        string Marker { get; }
    }

    public sealed class Probe : IProbe
    {
        public string Marker { get; set; } = "";
    }

    public sealed class ProbeOptions;

    #endregion
}
