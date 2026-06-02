using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

/// <summary>
/// DI wiring for keyed clients. The keyed registration must group its four services
/// (config / schema / validator / executor) under the supplied key so two clients pointing
/// at different servers coexist in one container instead of the second silently overriding
/// the first. These tests touch only registration and resolution — no server, no schema
/// fetch — so they stay fast and deterministic.
/// </summary>
[TestFixture]
public class KeyedRegistrationTests
{
    private static readonly Uri ServerB = new("http://server-b/graphql");
    private static readonly Uri ServerC = new("http://server-c/graphql");

    private static readonly Type[] KernelServices =
    [
        typeof(IGraphQLClientConfiguration),
        typeof(ISchemaProvider),
        typeof(IGraphQLClientValidator),
        typeof(IGraphQLClientExecutor),
    ];

    [Test]
    public void TwoKeys_ResolveDistinctExecutors()
    {
        var services = new ServiceCollection();
        services.AddKeyedTraxGraphQLClient("serverB", ServerB);
        services.AddKeyedTraxGraphQLClient("serverC", ServerC);

        using var sp = services.BuildServiceProvider();

        var b = sp.GetRequiredKeyedService<IGraphQLClientExecutor>("serverB");
        var c = sp.GetRequiredKeyedService<IGraphQLClientExecutor>("serverC");

        b.Should().NotBeSameAs(c, "each key must resolve its own executor");
        sp.GetRequiredKeyedService<IGraphQLClientValidator>("serverB")
            .Should()
            .NotBeSameAs(sp.GetRequiredKeyedService<IGraphQLClientValidator>("serverC"));
        sp.GetRequiredKeyedService<ISchemaProvider>("serverB")
            .Should()
            .NotBeSameAs(sp.GetRequiredKeyedService<ISchemaProvider>("serverC"));
    }

    [Test]
    public void EachKey_CarriesItsOwnBaseAddress()
    {
        var services = new ServiceCollection();
        services.AddKeyedTraxGraphQLClient("serverB", ServerB);
        services.AddKeyedTraxGraphQLClient("serverC", ServerC);

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredKeyedService<IGraphQLClientConfiguration>("serverB")
            .BaseAddress.Should()
            .Be(ServerB);
        sp.GetRequiredKeyedService<IGraphQLClientConfiguration>("serverC")
            .BaseAddress.Should()
            .Be(ServerC);
    }

    [Test]
    public void EachKey_GetsItsOwnConfiguredHttpClient()
    {
        var clientB = new HttpClient();
        var clientC = new HttpClient();
        var services = new ServiceCollection();
        services.AddKeyedTraxGraphQLClient("serverB", ServerB).ConfigureHttpClient(clientB);
        services.AddKeyedTraxGraphQLClient("serverC", ServerC).ConfigureHttpClient(clientC);

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredKeyedService<IGraphQLClientConfiguration>("serverB")
            .HttpClient.Should()
            .BeSameAs(clientB);
        sp.GetRequiredKeyedService<IGraphQLClientConfiguration>("serverC")
            .HttpClient.Should()
            .BeSameAs(clientC);
    }

    [Test]
    public void UnknownKey_ResolvesNull()
    {
        var services = new ServiceCollection();
        services.AddKeyedTraxGraphQLClient("serverB", ServerB);

        using var sp = services.BuildServiceProvider();

        sp.GetKeyedService<IGraphQLClientExecutor>("nope").Should().BeNull();
    }

    [Test]
    public void KeyedAndUnkeyed_CoexistInOneContainer()
    {
        var services = new ServiceCollection();
        services.AddTraxGraphQLClient(ServerB);
        services.AddKeyedTraxGraphQLClient("serverC", ServerC);

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IGraphQLClientConfiguration>().BaseAddress.Should().Be(ServerB);
        sp.GetRequiredKeyedService<IGraphQLClientConfiguration>("serverC")
            .BaseAddress.Should()
            .Be(ServerC);
    }

    [Test]
    public void KeyedRegistration_RegistersAllFourKernelServicesKeyed()
    {
        var services = new ServiceCollection();
        services.AddKeyedTraxGraphQLClient("serverB", ServerB);

        foreach (var serviceType in KernelServices)
        {
            services
                .Should()
                .ContainSingle(d =>
                    d.ServiceType == serviceType
                    && d.IsKeyedService
                    && Equals(d.ServiceKey, "serverB")
                )
                .Which.Lifetime.Should()
                .Be(
                    ServiceLifetime.Singleton,
                    "{0} must be registered as a keyed singleton",
                    serviceType.Name
                );
        }
    }

    [Test]
    public void AddKeyedTraxGraphQLClient_NullServiceKey_Throws()
    {
        var services = new ServiceCollection();
        var act = () => services.AddKeyedTraxGraphQLClient(null!, ServerB);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AddKeyedTraxGraphQLClient_NullBaseAddress_Throws()
    {
        var services = new ServiceCollection();
        var act = () => services.AddKeyedTraxGraphQLClient("serverB", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AddKeyedTraxGraphQLClient_NullServices_Throws()
    {
        IServiceCollection services = null!;
        var act = () => services.AddKeyedTraxGraphQLClient("serverB", ServerB);
        act.Should().Throw<ArgumentNullException>();
    }

    // UseIntrospection routes through the builder's ResolveConfiguration helper. Resolving the
    // provider runs the factory, so the schema provider is built against the right registration
    // (keyed vs unkeyed) without a network call.

    [Test]
    public void KeyedUseIntrospection_ResolvesIntrospectingProviderForThatKey()
    {
        var services = new ServiceCollection();
        services.AddKeyedTraxGraphQLClient("serverB", ServerB).UseIntrospection();

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredKeyedService<ISchemaProvider>("serverB")
            .Should()
            .BeOfType<IntrospectingSchemaProvider>();
    }

    [Test]
    public void UnkeyedUseIntrospection_ResolvesIntrospectingProvider()
    {
        var services = new ServiceCollection();
        services.AddTraxGraphQLClient(ServerB).UseIntrospection();

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<ISchemaProvider>().Should().BeOfType<IntrospectingSchemaProvider>();
    }

    // UseStartupValidation's hosted-service factory resolves the validator through the builder's
    // ResolveValidator helper. Resolving the hosted service runs that factory, so the keyed path
    // would throw if it resolved the (nonexistent) unkeyed validator.

    [Test]
    public void KeyedUseStartupValidation_ResolvesHostedServiceWithKeyedValidator()
    {
        var services = new ServiceCollection();
        services
            .AddKeyedTraxGraphQLClient("serverB", ServerB)
            .UseStartupValidation(typeof(KeyedRegistrationTests).Assembly);

        using var sp = services.BuildServiceProvider();

        sp.GetServices<IHostedService>()
            .OfType<GraphQLClientStartupValidator>()
            .Should()
            .ContainSingle();
    }

    [Test]
    public void UnkeyedUseStartupValidation_ResolvesHostedService()
    {
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(ServerB)
            .UseStartupValidation(typeof(KeyedRegistrationTests).Assembly);

        using var sp = services.BuildServiceProvider();

        sp.GetServices<IHostedService>()
            .OfType<GraphQLClientStartupValidator>()
            .Should()
            .ContainSingle();
    }

    // Keyed ValidateGraphQLClientAssembliesAsync overload.

    [Test]
    public void KeyedValidateAssemblies_UnknownKey_Throws()
    {
        var services = new ServiceCollection();
        services.AddKeyedTraxGraphQLClient("serverB", ServerB);
        using var sp = services.BuildServiceProvider();

        // No "nope" client registered, so resolving the keyed validator must fail. This proves
        // the overload resolves by the supplied key, not the unkeyed validator. The guard runs
        // synchronously, so assert against the invocation rather than the returned task.
        Action act = () =>
            _ = sp.ValidateGraphQLClientAssembliesAsync("nope", typeof(object).Assembly);
        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public async Task KeyedValidateAssemblies_ResolvesKeyedValidator_AndCompletes()
    {
        var services = new ServiceCollection();
        services.AddKeyedTraxGraphQLClient("serverB", ServerB);
        using var sp = services.BuildServiceProvider();

        // An assembly with no IGenericGraphQLClientRequest types resolves the keyed validator and
        // delegates to it without a schema fetch, covering the overload's happy path.
        await sp.Invoking(p =>
                p.ValidateGraphQLClientAssembliesAsync("serverB", typeof(object).Assembly)
            )
            .Should()
            .NotThrowAsync();
    }

    [Test]
    public void KeyedValidateAssemblies_NullServiceKey_Throws()
    {
        using var sp = new ServiceCollection().BuildServiceProvider();
        // Cast to object so the call binds to the keyed overload rather than the params-Assembly
        // one (a null first argument is otherwise ambiguous between them).
        Action act = () =>
            _ = sp.ValidateGraphQLClientAssembliesAsync((object)null!, typeof(object).Assembly);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void KeyedValidateAssemblies_NullServices_Throws()
    {
        IServiceProvider sp = null!;
        Action act = () =>
            _ = sp.ValidateGraphQLClientAssembliesAsync("serverB", typeof(object).Assembly);
        act.Should().Throw<ArgumentNullException>();
    }
}
