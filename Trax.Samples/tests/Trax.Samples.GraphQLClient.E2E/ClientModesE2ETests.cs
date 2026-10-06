using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Samples.GraphQLClient.Requests;
using Trax.Samples.GraphQLClient.Requests.A_RawString;
using Trax.Samples.GraphQLClient.Requests.D_Typed;
using Trax.Samples.GraphQLClient.Requests.E_Resource;
using Trax.Samples.GraphQLClient.Schema;

namespace Trax.Samples.GraphQLClient.E2E;

/// <summary>
/// The modes sample (<c>Trax.Samples.GraphQLClient</c>): one server and one client built from the
/// same <see cref="PlayerSchemaConfiguration"/>, asked for the same player through a raw string
/// (mode A), an embedded <c>.graphql</c> resource (mode E) and a POCO-derived typed request (mode
/// D, flat and through the <c>discover.players</c> path). The modes must agree, which is the
/// sample's whole claim.
/// </summary>
[TestFixture]
public class ClientModesE2ETests
{
    private WebApplication _server = null!;
    private ServiceProvider _clients = null!;
    private IGraphQLClientExecutor _executor = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<PlayerStore>();
        PlayerSchemaConfiguration.Configure(builder.Services.AddGraphQLServer());
        _server = builder.Build();
        _server.MapGraphQL("/graphql");
        await _server.StartAsync();

        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(new Uri("http://localhost/graphql"))
            .UseAssemblySchema(PlayerSchemaConfiguration.Configure)
            .ConfigureHttpClient(_server.GetTestClient());
        _clients = services.BuildServiceProvider();
        _executor = _clients.GetRequiredService<IGraphQLClientExecutor>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _clients.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Test]
    public async Task Raw_string_and_resource_modes_return_the_same_player()
    {
        var raw = await _executor.Run(new GetPlayerByRawStringRequest { Id = "player-1" });
        var resource = await _executor.Run(new GetPlayerByResourceRequest { Id = "player-1" });

        raw.Name.Should().Be("Aragorn");
        resource.Should().BeEquivalentTo(raw);
    }

    [Test]
    public async Task Typed_mode_returns_the_same_player_as_the_raw_string()
    {
        var raw = await _executor.Run(new GetPlayerByRawStringRequest { Id = "player-1" });
        var typed = await _executor.Run(new GetPlayerByTypedRequest { Id = "player-1" });

        typed.Id.Should().Be(raw.Id);
        typed.Name.Should().Be(raw.Name);
        typed.Level.Should().Be(raw.Level);
        typed.Inventory.Should().HaveCount(raw.Inventory.Count);
    }

    [Test]
    public async Task Typed_mode_reads_through_the_discover_path()
    {
        var nested = await _executor.Run(new LookupPlayerByNestedPathRequest { Id = "player-1" });

        nested.Should().NotBeNull();
        nested!.Name.Should().Be("Aragorn");
    }
}
