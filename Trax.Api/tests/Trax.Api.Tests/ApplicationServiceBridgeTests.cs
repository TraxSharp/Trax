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
    #region IsRegistered

    [Test]
    public void IsRegistered_ClosedRegistration_IsFound()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProbe, Probe>();

        ApplicationServiceBridge.IsRegistered<IProbe>(services).Should().BeTrue();
    }

    [Test]
    public void IsRegistered_MissingRegistration_IsNotFound()
    {
        ApplicationServiceBridge.IsRegistered<IProbe>(new ServiceCollection()).Should().BeFalse();
    }

    [Test]
    public void IsRegistered_OpenGenericRegistration_SatisfiesClosedRequest()
    {
        // AddLogging registers ILogger<> open. A closed ILogger<Probe> is resolvable from
        // it, so the bridge must consider it present.
        var services = new ServiceCollection();
        services.AddLogging();

        ApplicationServiceBridge.IsRegistered<ILogger<Probe>>(services).Should().BeTrue();
    }

    [Test]
    public void IsRegistered_OptionsOpenGeneric_SatisfiesClosedRequest()
    {
        var services = new ServiceCollection();
        services.AddOptions();

        ApplicationServiceBridge.IsRegistered<IOptions<ProbeOptions>>(services).Should().BeTrue();
    }

    [Test]
    public void IsRegistered_UnrelatedGeneric_IsNotFound()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEqualityComparer<string>>(StringComparer.Ordinal);

        ApplicationServiceBridge.IsRegistered<IComparer<string>>(services).Should().BeFalse();
    }

    [Test]
    public void IsRegistered_ImplementationTypeOnly_IsNotAServiceType()
    {
        // A descriptor registered as its concrete type does not satisfy the interface.
        var services = new ServiceCollection();
        services.AddSingleton<Probe>();

        ApplicationServiceBridge.IsRegistered<IProbe>(services).Should().BeFalse();
        ApplicationServiceBridge.IsRegistered<Probe>(services).Should().BeTrue();
    }

    #endregion

    #region BridgeApplicationService

    [Test]
    public async Task Bridge_RegisteredService_IsResolvableFromSchemaServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProbe>(new Probe { Marker = "from-app" });

        var builder = services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("ping").Type<StringType>().Resolve("pong"));
        builder.BridgeApplicationService<IProbe>(services);

        var executor = await BuildAsync(services);

        executor
            .Schema.Services.GetRequiredService<IProbe>()
            .Marker.Should()
            .Be("from-app", "the bridge must hand over the application container's instance");
    }

    [Test]
    public async Task Bridge_MissingService_DoesNotBreakSchemaBuild()
    {
        // The whole point of the conditional: an unconditional AddApplicationService here
        // would throw "No service for type ... has been registered" at schema build.
        var services = new ServiceCollection();

        var builder = services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("ping").Type<StringType>().Resolve("pong"));
        builder.BridgeApplicationService<IProbe>(services);

        var executor = await BuildAsync(services);

        executor.Schema.Services.GetService<IProbe>().Should().BeNull();
    }

    [Test]
    public void Bridge_ReturnsBuilder_ForChaining()
    {
        var services = new ServiceCollection();
        var builder = services.AddGraphQLServer();

        builder.BridgeApplicationService<IProbe>(services).Should().BeSameAs(builder);
    }

    [Test]
    public void Bridge_NullBuilder_Throws()
    {
        var act = () =>
            ((IRequestExecutorBuilder)null!).BridgeApplicationService<IProbe>(
                new ServiceCollection()
            );

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Bridge_NullServices_Throws()
    {
        var builder = new ServiceCollection().AddGraphQLServer();

        var act = () => builder.BridgeApplicationService<IProbe>(null!);

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
        builder.BridgeApplicationService<TraxApplicationServices>(services);

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
