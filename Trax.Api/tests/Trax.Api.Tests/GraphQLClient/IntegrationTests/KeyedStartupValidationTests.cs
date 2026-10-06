using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Api.Tests.GraphQLClient.Fixtures;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// A request names the client it belongs to with <c>[GraphQLClient(key)]</c>. Startup
/// validation for a keyed client checks only the requests marked with its key, and the
/// unkeyed client's only the unmarked ones, so two servers' requests can share an assembly.
/// A validation that finds nothing to check refuses to start, because that is a request
/// someone forgot to mark rather than a server with nothing to call.
///
/// <para>Guard for <c>docs/adr/0031-a-request-names-the-client-that-validates-it.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0031-a-request-names-the-client-that-validates-it.md")]
[TestFixture]
public class KeyedStartupValidationTests
{
    private const string PlayersKey = "keyed-players";
    private const string TraxKey = "keyed-trax";

    private GraphQLTestServerFixture _players = null!;
    private TraxServerFixture _trax = null!;

    [SetUp]
    public void SetUp()
    {
        _players = new GraphQLTestServerFixture();
        _trax = new TraxServerFixture();
    }

    [TearDown]
    public void TearDown()
    {
        _trax.Dispose();
        _players.Dispose();
    }

    [Test]
    public async Task Each_keyed_client_validates_only_the_requests_marked_with_its_key()
    {
        var services = new ServiceCollection();
        services
            .AddKeyedTraxGraphQLClient(PlayersKey, _players.BaseAddress)
            .ConfigureHttpClient(_players.CreateHttpClient())
            .UseStartupValidation(typeof(KeyedStartupValidationTests).Assembly);
        services
            .AddKeyedTraxGraphQLClient(TraxKey, _trax.BaseAddress)
            .ConfigureHttpClient(_trax.CreateHttpClient())
            .UseStartupValidation(typeof(KeyedStartupValidationTests).Assembly);
        // The test assembly also holds a request marked for this key; every key a request is
        // marked with needs a registered client.
        services.AddKeyedTraxGraphQLClient(ProbeServer.Billing, _players.BaseAddress);
        await using var sp = services.BuildServiceProvider();

        foreach (var hosted in sp.GetServices<IHostedService>())
            await hosted
                .Invoking(h => h.StartAsync(CancellationToken.None))
                .Should()
                .NotThrowAsync(
                    "docs/adr/0031-a-request-names-the-client-that-validates-it.md: a keyed client validates only the requests marked with its key"
                );
    }

    [Test]
    public async Task A_keyed_validation_with_no_request_marked_for_its_key_refuses_to_start()
    {
        var services = new ServiceCollection();
        services
            .AddKeyedTraxGraphQLClient("nobody-marks-this", _players.BaseAddress)
            .ConfigureHttpClient(_players.CreateHttpClient())
            .UseStartupValidation(typeof(KeyedStartupValidationTests).Assembly);
        await using var sp = services.BuildServiceProvider();
        var hosted = sp.GetServices<IHostedService>().Single();

        await hosted
            .Invoking(h => h.StartAsync(CancellationToken.None))
            .Should()
            .ThrowAsync<InvalidOperationException>(
                "docs/adr/0031-a-request-names-the-client-that-validates-it.md: a validation with nothing to check refuses to start"
            )
            .WithMessage("*nobody-marks-this*[GraphQLClient*");
    }

    [Test]
    public async Task The_keyed_validation_helper_validates_only_requests_marked_with_its_key()
    {
        var services = new ServiceCollection();
        services
            .AddKeyedTraxGraphQLClient(PlayersKey, _players.BaseAddress)
            .ConfigureHttpClient(_players.CreateHttpClient());
        // The other keys the test assembly's requests are marked with.
        services.AddKeyedTraxGraphQLClient(TraxKey, _trax.BaseAddress);
        services.AddKeyedTraxGraphQLClient(ProbeServer.Billing, _players.BaseAddress);
        await using var sp = services.BuildServiceProvider();

        await sp.Invoking(p =>
                p.ValidateGraphQLClientAssembliesAsync(
                    PlayersKey,
                    typeof(KeyedStartupValidationTests).Assembly
                )
            )
            .Should()
            .NotThrowAsync();
    }

    [Test]
    public void A_request_belongs_to_the_client_its_attribute_names()
    {
        GraphQLClientAttribute
            .BelongsTo(typeof(KeyedPlayersProbeRequest), PlayersKey)
            .Should()
            .BeTrue();
        GraphQLClientAttribute
            .BelongsTo(typeof(KeyedPlayersProbeRequest), TraxKey)
            .Should()
            .BeFalse();
        GraphQLClientAttribute.BelongsTo(typeof(KeyedPlayersProbeRequest), null).Should().BeFalse();
        GraphQLClientAttribute.BelongsTo(typeof(UnmarkedProbeRequest), null).Should().BeTrue();
        GraphQLClientAttribute
            .BelongsTo(typeof(UnmarkedProbeRequest), PlayersKey)
            .Should()
            .BeFalse();
    }

    [Test]
    public void A_non_string_key_matches_by_value()
    {
        GraphQLClientAttribute
            .BelongsTo(typeof(EnumKeyedProbeRequest), ProbeServer.Billing)
            .Should()
            .BeTrue();
        GraphQLClientAttribute
            .BelongsTo(typeof(EnumKeyedProbeRequest), "Billing")
            .Should()
            .BeFalse();
    }
}

public enum ProbeServer
{
    Billing,
}

[GraphQLClient("keyed-players")]
public sealed class KeyedPlayersProbeRequest : IGraphQLClientRequest<object>
{
    public string Query => "query KeyedPlayersProbe { allItems { id } }";
}

[GraphQLClient("keyed-trax")]
public sealed class KeyedTraxProbeRequest : IGraphQLClientRequest<object>
{
    public string Query => "query KeyedTraxProbe { discover { netsuiteClient { __typename } } }";
}

[GraphQLClient(ProbeServer.Billing)]
public sealed class EnumKeyedProbeRequest : IGraphQLClientRequest<object>
{
    public string Query => "query EnumKeyedProbe { __typename }";
}

public sealed class UnmarkedProbeRequest : IGraphQLClientRequest<object>
{
    public string Query => "query UnmarkedProbe { __typename }";
}
