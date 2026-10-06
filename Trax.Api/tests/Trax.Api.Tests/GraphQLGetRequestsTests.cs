using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// GraphQL over HTTP GET is refused unless the host opts in with <c>AllowGetRequests()</c>, and
/// even then a GET must carry the <c>GraphQL-preflight</c> header. A cross-site navigation carries
/// the caller's <c>SameSite=Lax</c> cookie but cannot add a header, so neither default lets another
/// site run a query as the signed-in user.
/// </summary>
/// <remarks>
/// Guard for <c>docs/adr/0024-graphql-get-is-off-unless-the-host-opts-in.md</c>. The endpoint is
/// self-mapped with <c>MapGraphQL(path, "trax")</c>, which is all <c>UseTraxGraphQL</c> does, so
/// the setting is shown to belong to the schema and to hold however the host maps it.
/// </remarks>
[TestFixture]
[Property("adr", "docs/adr/0024-graphql-get-is-off-unless-the-host-opts-in.md")]
public class GraphQLGetRequestsTests
{
    private const string Path = "/trax/graphql";
    private const string QueryString = "?query=%7B__typename%7D";

    [Test]
    public async Task A_GET_query_is_refused_by_default()
    {
        using var host = await StartHostAsync(graphql => graphql);
        var client = host.GetTestClient();

        var response = await client.SendAsync(Get(Path + QueryString, preflight: true));

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync())
            .Should()
            .NotContain(
                "__typename",
                "docs/adr/0024-graphql-get-is-off-unless-the-host-opts-in.md: GraphQL GET is off unless the host opts in with AllowGetRequests()"
            );
    }

    [Test]
    public async Task A_POST_query_is_served_by_default()
    {
        using var host = await StartHostAsync(graphql => graphql);
        var client = host.GetTestClient();

        var response = await client.PostAsync(
            Path,
            new StringContent("""{"query":"{ __typename }"}""", Encoding.UTF8, "application/json")
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("__typename");
    }

    [Test]
    public async Task With_the_opt_in_a_GET_query_with_the_preflight_header_is_served()
    {
        using var host = await StartHostAsync(graphql => graphql.AllowGetRequests());
        var client = host.GetTestClient();

        var response = await client.SendAsync(Get(Path + QueryString, preflight: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("__typename");
    }

    [Test]
    public async Task With_the_opt_in_a_GET_query_without_the_preflight_header_is_refused()
    {
        using var host = await StartHostAsync(graphql => graphql.AllowGetRequests());
        var client = host.GetTestClient();

        var response = await client.SendAsync(Get(Path + QueryString, preflight: false));

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync())
            .Should()
            .NotContain(
                "\"__typename\":",
                "docs/adr/0024-graphql-get-is-off-unless-the-host-opts-in.md: an opted-in GET still needs the GraphQL-preflight header, which a "
                    + "cross-site navigation cannot add"
            );
    }

    [Test]
    public async Task With_the_opt_in_a_GET_mutation_is_refused()
    {
        using var host = await StartHostAsync(graphql => graphql.AllowGetRequests());
        var client = host.GetTestClient();

        var response = await client.SendAsync(
            Get(Path + "?query=mutation%7B__typename%7D", preflight: true)
        );

        // HotChocolate answers with a GraphQL error rather than a status code: no data, no execution.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("operation kind is not allowed");
        body.Should().NotContain("\"data\"");
    }

    [Test]
    public async Task The_IDE_page_is_still_served_with_GET_off()
    {
        // The IDE follows the introspection decision, which is off outside Development unless
        // the host allows it; this test is about GET, so it allows introspection.
        using var host = await StartHostAsync(graphql => graphql.AllowIntrospection(_ => true));
        var client = host.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, Path + "/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    private static HttpRequestMessage Get(string uri, bool preflight)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (preflight)
            request.Headers.Add("GraphQL-preflight", "1");
        return request;
    }

    private static async Task<IHost> StartHostAsync(
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder> configure
    )
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddSingleton<TraxMarker>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([]);
                        services.AddSingleton(discovery);
                        services.AddSingleton(Substitute.For<IEffectRegistry>());
                        // Something must be queryable for the schema to build; only __typename is read.
                        services.AddTraxGraphQL(graphql =>
                            configure(
                                graphql
                                    .ExposeOperationQueries()
                                    .ExposeOperationMutations()
                                    .AllowAnonymousOperations()
                            )
                        );
                        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
                        services.AddScoped(_ => Substitute.For<IOperationsService>());
                        services.AddScoped(_ =>
                            Substitute.For<Trax.Mediator.Services.TrainExecution.ITrainExecutionService>()
                        );
                        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
                        services.AddScoped(_ => Substitute.For<IJobSubmitter>());
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        // UseTraxGraphQL maps exactly this; mapping it directly is the self-mapped
                        // form, so the setting is shown to live on the schema, not the mapping.
                        app.UseEndpoints(e => e.MapGraphQL(Path, "trax"));
                    })
            )
            .Build();

        await host.StartAsync();
        return host;
    }
}
