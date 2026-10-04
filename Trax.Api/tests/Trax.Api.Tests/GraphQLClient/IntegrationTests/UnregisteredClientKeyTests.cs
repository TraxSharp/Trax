using System.Reflection;
using System.Reflection.Emit;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// Every key a <c>[GraphQLClient(key)]</c> mark names must have a client registered under it.
/// A request marked with a key nothing registers would be validated by no client, so a host that
/// validates the assembly holding it refuses to start, naming the request and the key.
///
/// <para>Guard for <c>docs/adr/0031-a-request-names-the-client-that-validates-it.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0031-a-request-names-the-client-that-validates-it.md")]
[TestFixture]
public class UnregisteredClientKeyTests
{
    private const string Adr = "docs/adr/0031-a-request-names-the-client-that-validates-it.md";

    private string _schema = null!;

    [SetUp]
    public async Task SetUp()
    {
        _schema = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"keys-{Guid.NewGuid():N}.graphql"
        );
        await File.WriteAllTextAsync(_schema, "type Query { ping: String }");
    }

    [TearDown]
    public void TearDown() => File.Delete(_schema);

    [Test]
    public async Task A_request_marked_with_a_key_no_client_has_refuses_startup()
    {
        var requests = Requests(("Unmarked", null), ("Mistyped", "biling"));
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(new Uri("http://localhost/graphql"))
            .UseFileSchema(_schema)
            .UseStartupValidation(requests);
        await using var sp = services.BuildServiceProvider();

        await sp.GetServices<IHostedService>()
            .Single()
            .Invoking(h => h.StartAsync(CancellationToken.None))
            .Should()
            .ThrowAsync<InvalidOperationException>(Adr)
            .WithMessage("*Mistyped*biling*");
    }

    [Test]
    public async Task A_keyed_client_refuses_startup_for_a_key_beside_its_own()
    {
        var requests = Requests(("Billing", "billing"), ("Mistyped", "biling"));
        var services = new ServiceCollection();
        services
            .AddKeyedTraxGraphQLClient("billing", new Uri("http://localhost/graphql"))
            .UseFileSchema(_schema)
            .UseStartupValidation(requests);
        await using var sp = services.BuildServiceProvider();

        await sp.GetServices<IHostedService>()
            .Single()
            .Invoking(h => h.StartAsync(CancellationToken.None))
            .Should()
            .ThrowAsync<InvalidOperationException>(Adr)
            .WithMessage("*Mistyped*biling*");
    }

    [Test]
    public async Task Every_key_registered_starts()
    {
        var requests = Requests(("Unmarked", null), ("Billing", "billing"));
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(new Uri("http://localhost/graphql"))
            .UseFileSchema(_schema)
            .UseStartupValidation(requests);
        services
            .AddKeyedTraxGraphQLClient("billing", new Uri("http://localhost/billing"))
            .UseFileSchema(_schema)
            .UseStartupValidation(requests);
        await using var sp = services.BuildServiceProvider();

        foreach (var hosted in sp.GetServices<IHostedService>())
            await hosted
                .Invoking(h => h.StartAsync(CancellationToken.None))
                .Should()
                .NotThrowAsync();
    }

    [Test]
    public async Task The_validation_helpers_refuse_a_key_no_client_has()
    {
        var requests = Requests(("Unmarked", null), ("Billing", "billing"), ("Mistyped", "biling"));
        var services = new ServiceCollection();
        services.AddTraxGraphQLClient(new Uri("http://localhost/graphql")).UseFileSchema(_schema);
        services
            .AddKeyedTraxGraphQLClient("billing", new Uri("http://localhost/billing"))
            .UseFileSchema(_schema);
        await using var sp = services.BuildServiceProvider();

        await sp.Invoking(p => p.ValidateGraphQLClientAssembliesAsync(requests))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Mistyped*biling*");
        await sp.Invoking(p => p.ValidateGraphQLClientAssembliesAsync("billing", requests))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Mistyped*biling*");
    }

    /// <summary>
    /// An assembly of its own holding one request per entry, each marked with its key (or not),
    /// so the marks every other test leaves in the test assembly play no part.
    /// </summary>
    private static Assembly Requests(params (string Name, string? Key)[] requests)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"ClientKeyProbes{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.Run
        );
        var module = assembly.DefineDynamicModule("ClientKeyProbes");
        foreach (var (name, key) in requests)
        {
            var type = module.DefineType(
                $"ClientKeyProbes.{name}Request",
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
                typeof(PingProbeRequest)
            );
            type.DefineDefaultConstructor(MethodAttributes.Public);
            if (key is not null)
                type.SetCustomAttribute(
                    new CustomAttributeBuilder(
                        typeof(GraphQLClientAttribute).GetConstructor([typeof(object)])!,
                        [key]
                    )
                );
            type.CreateType();
        }
        return assembly;
    }
}

/// <summary>The base each generated probe request derives from.</summary>
public class PingProbeRequest : IGraphQLClientRequest<object>
{
    public string Query => "query Ping { ping }";
}
