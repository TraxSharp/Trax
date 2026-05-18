using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Trax.Api.Tests.GraphQLClient.Fixtures;

/// <summary>
/// Spins up an in-process HotChocolate GraphQL server at <c>/graphql</c> via
/// <see cref="TestServer"/>. Returns the test server's <see cref="HttpClient"/> so client-side
/// tests hit a real schema, real validation pipeline, and real resolvers without any
/// network nondeterminism.
/// </summary>
public sealed class GraphQLTestServerFixture : IDisposable
{
    public TestServer Server { get; }
    public TestPlayerStore PlayerStore { get; }
    public Uri BaseAddress { get; } = new Uri("http://localhost/graphql");

    public GraphQLTestServerFixture()
    {
        PlayerStore = new TestPlayerStore();

        var hostBuilder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddSingleton(PlayerStore);
                services
                    .AddGraphQLServer()
                    .AddQueryType<TestQuery>()
                    .AddMutationType<TestMutation>()
                    .DisableIntrospection(false)
                    .ModifyRequestOptions(o => o.IncludeExceptionDetails = true);
                services.AddRouting();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapGraphQL("/graphql"));
            });
        });

        var host = hostBuilder.Start();
        Server = host.GetTestServer();
    }

    public HttpClient CreateHttpClient()
    {
        var client = Server.CreateClient();
        client.BaseAddress = BaseAddress;
        return client;
    }

    public void Dispose()
    {
        Server.Dispose();
    }
}
