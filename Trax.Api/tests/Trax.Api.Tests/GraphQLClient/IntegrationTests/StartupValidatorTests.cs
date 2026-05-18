using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Api.Tests.GraphQLClient.Fixtures;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// The startup validator is the boot gate: schema drift becomes a startup failure, not a
/// runtime 400 hours later. These tests pin down: success path runs without throwing,
/// failure path surfaces the offending query, and the DI extension actually registers the
/// hosted service.
/// </summary>
[TestFixture]
public class StartupValidatorTests
{
    private GraphQLTestServerFixture _fixture = null!;

    [SetUp]
    public void SetUp() => _fixture = new GraphQLTestServerFixture();

    [TearDown]
    public void TearDown() => _fixture.Dispose();

    [Test]
    public async Task StartAsync_AllRequestsValid_CompletesWithoutThrowing()
    {
        var config = new GraphQLClientConfigurationBuilder(_fixture.BaseAddress)
        {
            HttpClient = _fixture.CreateHttpClient(),
        }.Build();
        var validator = new GraphQLClientValidator(new IntrospectingSchemaProvider(config));

        // Restrict to a single known-good fake to keep the validator from hitting the
        // deliberately broken DriftedQueryRequest.
        var v = new GraphQLClientStartupValidator(
            validator,
            new[] { typeof(AllItemsRequest).Assembly },
            t => t == typeof(AllItemsRequest)
        );

        var act = async () => await v.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task StartAsync_DriftedRequest_ThrowsValidationException()
    {
        var config = new GraphQLClientConfigurationBuilder(_fixture.BaseAddress)
        {
            HttpClient = _fixture.CreateHttpClient(),
        }.Build();
        var validator = new GraphQLClientValidator(new IntrospectingSchemaProvider(config));

        var v = new GraphQLClientStartupValidator(
            validator,
            new[] { typeof(GetPlayerByDriftedQueryRequest).Assembly },
            t => t == typeof(GetPlayerByDriftedQueryRequest)
        );

        var act = async () => await v.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<GraphQLValidationException>();
    }

    [Test]
    public async Task StopAsync_Always_Completes()
    {
        var v = new GraphQLClientStartupValidator(null!, Array.Empty<Assembly>(), null);

        await v.StopAsync(CancellationToken.None);
    }

    [Test]
    public void Builder_UseStartupValidation_RegistersHostedService()
    {
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(new Uri("http://localhost/graphql"))
            .UseStartupValidation(typeof(AllItemsRequest).Assembly);

        var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();

        hostedServices.Should().NotBeEmpty();
    }

    [Test]
    public void Builder_UseStartupValidation_NoAssemblies_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddTraxGraphQLClient(new Uri("http://localhost/graphql"));

        var act = () => builder.UseStartupValidation();
        act.Should().Throw<ArgumentException>();
    }
}
