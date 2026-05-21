using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.Tests.GraphQLClient.Fakes;
using Trax.Api.Tests.GraphQLClient.Fixtures;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes.TraxServer;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// The other typed-request tests run against a hand-built HotChocolate type that MODELS the
/// <c>discover.{namespace}</c> envelope. This suite runs against the actual envelope a real
/// Trax server emits when trains are decorated with <c>[TraxQuery(Namespace = "...")]</c> /
/// <c>[TraxMutation(Namespace = "...")]</c>. If the server-side convention and the client-side
/// Path attribute ever drift — different casing, different wrapper field name, different
/// mutation response shape — these tests fail where every other test would pass.
///
/// In-memory effect provider only, so no database required.
/// </summary>
[TestFixture]
public class TraxServerE2ETests
{
    private TraxServerFixture _fixture = null!;
    private ServiceProvider _services = null!;
    private IGraphQLClientExecutor _executor = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new TraxServerFixture();
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(_fixture.BaseAddress)
            .ConfigureHttpClient(_fixture.CreateHttpClient());
        _services = services.BuildServiceProvider();
        _executor = _services.GetRequiredService<IGraphQLClientExecutor>();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        _fixture.Dispose();
    }

    [Test]
    public async Task TypedQuery_ThroughDiscoverNamespace_ReachesRealTrain()
    {
        // The whole point: prove the convention pairing holds end-to-end. The server emits
        // discover.netsuiteClient.lookupCustomer (from [TraxQuery(Namespace = "netsuiteClient")]),
        // the client sends Path = "discover.netsuiteClient", RootField = "lookupCustomer".
        // The train runs, returns its output, and the response unwraps through the same path.
        var result = await _executor.Run(
            new LookupCustomerThroughTraxRequest
            {
                Input = new LookupCustomerInput { Email = "acme@example.com" },
            }
        );

        result.Should().NotBeNull();
        result.Email.Should().Be("acme@example.com");
        result.Id.Should().StartWith("cust-");
        result.CreditLimit.Should().Be(50_000);
    }

    [Test]
    public async Task TypedMutation_ThroughDispatchNamespace_ReachesRealTrain()
    {
        // Mutation half of the convention pairing. Trax wraps mutation outputs in a
        // {trainName}Response object (externalId + metadataId + output). The typed client
        // declares that wrapper as its TResponse so the extractor walks
        // dispatch.netsuiteClient.updateCreditLimit and deserializes the wrapper.
        var result = await _executor.Run(
            new UpdateCreditLimitThroughTraxRequest
            {
                Input = new UpdateCreditLimitInput { CustomerId = "cust-123", NewLimit = 75_000 },
            }
        );

        result.Should().NotBeNull();
        result.ExternalId.Should().NotBeNullOrEmpty();
        result.Output.Should().NotBeNull();
        result.Output!.CustomerId.Should().Be("cust-123");
        result.Output.NewLimit.Should().Be(75_000);
        result.Output.OldLimit.Should().Be(50_000);
    }
}
