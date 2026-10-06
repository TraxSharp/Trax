using System.Text.Json;
using Trax.Api.Auth.Jwt.Testing;
using Trax.Samples.Auth.Auth;
using Trax.Samples.Auth.E2E.Fixtures;
using Trax.Samples.Auth.E2E.Utilities;

namespace Trax.Samples.Auth.E2E.Tests;

/// <summary>
/// A WebSocket carries its credential in <c>connection_init</c>: a JWT or an API key under
/// <c>authToken</c>. A connection without a valid one is refused, and on a host that exposes the
/// operations surface only an operator receives the lifecycle stream of every train.
/// </summary>
[TestFixture]
public class SubscriptionAuthTests : AuthTestFixture
{
    private const string Completed = "subscription { onTrainCompleted { trainName trainState } }";

    private Microsoft.AspNetCore.TestHost.WebSocketClient NewSocket() =>
        SharedAuthSetup.Factory.Server.CreateWebSocketClient();

    [Test]
    public async Task Jwt_InConnectionInit_IsAccepted()
    {
        await using var socket = await GraphQLWebSocketClient.ConnectAsync(
            NewSocket(),
            new { authToken = AliceToken.BearerToken }
        );
    }

    [Test]
    public async Task ApiKey_InConnectionInit_IsAccepted()
    {
        await using var socket = await GraphQLWebSocketClient.ConnectAsync(
            NewSocket(),
            new { authToken = DemoCredentials.AliceKey }
        );
    }

    [Test]
    public async Task NoCredential_IsRejected()
    {
        var connect = () => GraphQLWebSocketClient.ConnectAsync(NewSocket(), payload: null);

        await connect.Should().ThrowAsync<ConnectionRejectedException>();
    }

    [Test]
    public async Task UnknownKey_IsRejected()
    {
        var connect = () =>
            GraphQLWebSocketClient.ConnectAsync(NewSocket(), new { authToken = "not-a-key" });

        await connect.Should().ThrowAsync<ConnectionRejectedException>();
    }

    [Test]
    public async Task ForgedJwt_IsRejected()
    {
        var forged = TestTokenIssuer
            .Symmetric(
                DemoCredentials.JwtIssuer,
                DemoCredentials.JwtAudience,
                "a-different-key-of-at-least-32-bytes!!"u8.ToArray()
            )
            .Mint(b => b.WithSubject("oscar").WithRole(AuthRoles.Operator));

        var connect = () =>
            GraphQLWebSocketClient.ConnectAsync(NewSocket(), new { authToken = forged });

        await connect.Should().ThrowAsync<ConnectionRejectedException>();
    }

    [Test]
    public async Task Reader_IsRefused_WhenSubscribingToEveryTrain()
    {
        await using var socket = await GraphQLWebSocketClient.ConnectAsync(
            NewSocket(),
            new { authToken = BobToken.BearerToken }
        );

        await socket.SubscribeAsync("bob", Completed);
        var reply = await socket.ReceiveAsync(TimeSpan.FromSeconds(10));

        reply.GetProperty("type").GetString().Should().BeOneOf("error", "next");
        reply
            .GetRawText()
            .Should()
            .Contain("TRAX_AUTHORIZATION", "no train here is [TraxBroadcast], so Bob may see none");
    }

    [Test]
    public async Task Operator_ReceivesEveryTrain()
    {
        await using var socket = await GraphQLWebSocketClient.ConnectAsync(
            NewSocket(),
            new { authToken = OscarToken.BearerToken }
        );
        await socket.SubscribeAsync("oscar", Completed);

        // graphql-transport-ws does not acknowledge a subscribe, so trigger until one arrives.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        JsonElement? received = null;
        while (received is null && DateTime.UtcNow < deadline)
        {
            var echo = await GraphQL.SendAsync(
                """{ discover { echo(input: { message: "ping" }) { echoed } } }""",
                Anonymous
            );
            echo.HasErrors.Should().BeFalse(echo.Raw);

            try
            {
                var message = await socket.ReceiveAsync(TimeSpan.FromSeconds(2));
                message.GetProperty("type").GetString().Should().Be("next", message.GetRawText());
                received = message
                    .GetProperty("payload")
                    .GetProperty("data")
                    .GetProperty("onTrainCompleted");
            }
            catch (TimeoutException)
            {
                // Not subscribed yet, or this event raced the subscribe: trigger again.
            }
        }

        received
            .Should()
            .NotBeNull("an operator subscribed to every train should see one complete");
        received!.Value.GetProperty("trainState").GetString().Should().Be("COMPLETED");
    }
}
