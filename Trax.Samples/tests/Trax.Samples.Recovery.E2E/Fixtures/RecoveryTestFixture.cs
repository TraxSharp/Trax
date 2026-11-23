using System.Text.Json;
using Trax.Core.Extensions;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.E2E.Utilities;

namespace Trax.Samples.Recovery.E2E.Fixtures;

/// <summary>
/// Gives each test an HTTP GraphQL client, a live <c>onJunctionEvent</c> connection with the operator
/// key, and a <see cref="DemoRun"/> to drive.
/// </summary>
public abstract class RecoveryTestFixture
{
    protected const string OperatorKey = DemoKeys.Operator;

    protected HttpClient Http
    {
        get => field.NotNull(nameof(Http));
        private set;
    }
    protected GraphQLClient GraphQL
    {
        get => field.NotNull(nameof(GraphQL));
        private set;
    }
    protected JunctionEventStream Stream
    {
        get => field.NotNull(nameof(Stream));
        private set;
    }
    protected DemoRun Run
    {
        get => field.NotNull(nameof(Run));
        private set;
    }
    protected CountingDecider Decider => SharedRecoverySetup.Factory.Decider;

    [SetUp]
    public async Task OpenConnections()
    {
        Http = SharedRecoverySetup.Factory.CreateClient();
        GraphQL = new GraphQLClient(Http);
        Stream = await JunctionEventStream.ConnectAsync(
            SharedRecoverySetup.Factory.Server.CreateWebSocketClient(),
            OperatorKey
        );
        Run = new DemoRun(GraphQL, Stream, OperatorKey);
    }

    [TearDown]
    public async Task CloseConnections()
    {
        await Stream.DisposeAsync();
        Http.Dispose();
    }

    protected static IEnumerable<JsonElement> Questions(IEnumerable<JsonElement> steps) =>
        steps.Where(s => s.GetProperty("kind").GetString() is "CHOICE" or "SCORE" or "YES_NO");

    protected static IEnumerable<string> Names(IEnumerable<JsonElement> steps) =>
        steps.Select(s => s.GetProperty("name").GetString().NotNull());

    protected static int Attempt(JsonElement step) => step.GetProperty("attempt").GetInt32();
}
