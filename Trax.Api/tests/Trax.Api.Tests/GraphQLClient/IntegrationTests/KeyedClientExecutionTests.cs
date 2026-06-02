using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Api.Tests.GraphQLClient.Fakes;
using Trax.Api.Tests.GraphQLClient.Fixtures;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes.TraxServer;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// Two real, differently-schema'd GraphQL servers behind two keyed clients in ONE container:
/// the player-schema HotChocolate server (key "players") and the real Trax server (key
/// "netsuite"). Proves the whole point of keyed registration — both coexist, each validates
/// against its OWN schema, and a query meant for one server is rejected when run through the
/// other key's executor. Both servers run in-memory (no database).
/// </summary>
[TestFixture]
public class KeyedClientExecutionTests
{
    private const string PlayersKey = "players";
    private const string NetsuiteKey = "netsuite";

    private GraphQLTestServerFixture _players = null!;
    private TraxServerFixture _netsuite = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _players = new GraphQLTestServerFixture();
        _netsuite = new TraxServerFixture();

        var services = new ServiceCollection();
        services
            .AddKeyedTraxGraphQLClient(PlayersKey, _players.BaseAddress)
            .ConfigureHttpClient(_players.CreateHttpClient());
        services
            .AddKeyedTraxGraphQLClient(NetsuiteKey, _netsuite.BaseAddress)
            .ConfigureHttpClient(_netsuite.CreateHttpClient());

        _services = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        _netsuite.Dispose();
        _players.Dispose();
    }

    private IGraphQLClientExecutor Executor(string key) =>
        _services.GetRequiredKeyedService<IGraphQLClientExecutor>(key);

    [Test]
    public async Task PlayersKey_RunsPlayerQuery_AgainstPlayerServer()
    {
        var items = await Executor(PlayersKey).Run(new AllItemsRequest());

        items.Should().NotBeEmpty();
        items.Select(i => i.Name).Should().Contain("Sword");
    }

    [Test]
    public async Task NetsuiteKey_RunsCustomerQuery_AgainstTraxServer()
    {
        var customer = await Executor(NetsuiteKey)
            .Run(
                new LookupCustomerThroughTraxRequest
                {
                    Input = new LookupCustomerInput { Email = "acme@example.com" },
                }
            );

        customer.Email.Should().Be("acme@example.com");
        customer.CreditLimit.Should().Be(50_000);
    }

    [Test]
    public async Task CustomerQuery_ThroughPlayersKey_FailsSchemaValidation()
    {
        // discover.netsuiteClient.lookupCustomer exists on the Trax server, NOT on the player
        // schema. The "players" key validates against the player schema and must reject it
        // before any HTTP call. This is the isolation guarantee keying provides.
        var act = async () =>
            await Executor(PlayersKey)
                .Run(
                    new LookupCustomerThroughTraxRequest
                    {
                        Input = new LookupCustomerInput { Email = "acme@example.com" },
                    }
                );

        await act.Should().ThrowAsync<GraphQLValidationException>();
    }

    [Test]
    public void EachKey_ResolvesDistinctValidatorInstances()
    {
        var playersValidator = _services.GetRequiredKeyedService<IGraphQLClientValidator>(
            PlayersKey
        );
        var netsuiteValidator = _services.GetRequiredKeyedService<IGraphQLClientValidator>(
            NetsuiteKey
        );

        playersValidator.Should().NotBeSameAs(netsuiteValidator);
    }

    [Test]
    public void KeyedUseFileSchema_ReplacesOnlyThatKeysSchemaProvider()
    {
        var sdlPath = Path.Combine(Path.GetTempPath(), $"keyed-schema-{Guid.NewGuid():N}.graphql");
        File.WriteAllText(sdlPath, "schema { query: Query }\ntype Query { ping: String! }");
        try
        {
            var services = new ServiceCollection();
            services
                .AddKeyedTraxGraphQLClient(PlayersKey, _players.BaseAddress)
                .UseFileSchema(sdlPath);
            services.AddKeyedTraxGraphQLClient(NetsuiteKey, _netsuite.BaseAddress);

            using var sp = services.BuildServiceProvider();

            sp.GetRequiredKeyedService<ISchemaProvider>(PlayersKey)
                .Should()
                .BeOfType<FileSchemaProvider>("UseFileSchema must replace only the keyed provider");
            sp.GetRequiredKeyedService<ISchemaProvider>(NetsuiteKey)
                .Should()
                .BeOfType<IntrospectingSchemaProvider>(
                    "the other key keeps the introspection default"
                );
        }
        finally
        {
            File.Delete(sdlPath);
        }
    }

    [Test]
    public async Task KeyedStartupValidation_ValidatesAgainstThatKeysSchema()
    {
        // The keyed validator drives startup validation. A request valid on the player schema
        // passes; a request from the other server's schema fails — proving UseStartupValidation
        // resolved the validator by key, not the (nonexistent) unkeyed one.
        var validator = _services.GetRequiredKeyedService<IGraphQLClientValidator>(PlayersKey);

        var valid = new GraphQLClientStartupValidator(
            validator,
            [typeof(AllItemsRequest).Assembly],
            typeFilter: t => t == typeof(AllItemsRequest)
        );
        await FluentActions
            .Awaiting(() => valid.StartAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync();

        var crossSchema = new GraphQLClientStartupValidator(
            validator,
            [typeof(LookupCustomerThroughTraxRequest).Assembly],
            typeFilter: t => t == typeof(LookupCustomerThroughTraxRequest)
        );
        await FluentActions
            .Awaiting(() => crossSchema.StartAsync(CancellationToken.None))
            .Should()
            .ThrowAsync<GraphQLValidationException>();
    }
}
