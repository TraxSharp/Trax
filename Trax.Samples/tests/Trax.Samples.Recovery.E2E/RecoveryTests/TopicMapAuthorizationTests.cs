using System.Net;
using System.Reflection;
using Trax.Effect.Attributes;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Trains.Topics;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// Who may build a topic map. Every event entering <c>Building</c> declares its own rule, and the startup
/// comparison holds the train the state invokes to the strictest of them, so the train can ask for the
/// <c>Operator</c> role again and the host still starts.
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class TopicMapAuthorizationTests
{
    [Test]
    public async Task BuildTopicMapTrain_has_its_Operator_roles_back_and_the_host_starts()
    {
        var authorize = typeof(BuildTopicMapTrain).GetCustomAttribute<TraxAuthorizeAttribute>();

        authorize.Should().NotBeNull();
        (authorize!.Roles ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Should()
            .Contain(
                RecoveryRoles.Operator,
                "an event entering Building declares Operator, so the startup comparison admits an "
                    + "Operator train (Trax.Docs/adr/0048)"
            );

        using var http = SharedRecoverySetup.Factory.CreateClient();
        using var health = await http.GetAsync("/trax/health");
        health.StatusCode.Should().Be(HttpStatusCode.OK, "the host started with the roles back");
    }
}
