using System.Text.Json.Nodes;
using Trax.Samples.StateMachine.E2E.Utilities;

namespace Trax.Samples.StateMachine.E2E.Fixtures;

/// <summary>
/// Base for the tests against the shared Development host: a client for the four <c>stateMachine</c>
/// mutations, the demo callers, and builders for the snapshots the sample's web client would write.
/// Every test uses a fresh draft id and, for the checkout, an item name no other test uses, so tests
/// share one database without seeing each other's drafts or charges.
/// </summary>
public abstract class StateMachineTestFixture
{
    protected const string Turnstile = "turnstile";

    protected const string Checkout = "checkout";

    protected HttpClient Http { get; private set; } = null!;

    protected GraphQLClient GraphQL { get; private set; } = null!;

    protected StateMachineClient Machines { get; private set; } = null!;

    protected static Caller Anonymous => Caller.Anonymous;

    protected static Caller Alice => Caller.Alice;

    protected static Caller Bob => Caller.Bob;

    protected static RecordingCharge Charges => SharedStateMachineSetup.Factory.Charges;

    [OneTimeSetUp]
    public void CreateClients()
    {
        Http = SharedStateMachineSetup.Factory.CreateClient();
        GraphQL = new GraphQLClient(Http);
        Machines = new StateMachineClient(GraphQL);
    }

    [OneTimeTearDown]
    public void DisposeClients() => Http.Dispose();

    /// <summary>An item name no other test puts in a cart.</summary>
    protected static string UniqueItem(string name) => $"{name}-{Guid.NewGuid():N}";

    /// <summary>A turnstile snapshot on the wire.</summary>
    protected static string TurnstileWire(string state, JsonObject? context = null) =>
        Wire(Turnstile, 1, state, context ?? []);

    /// <summary>
    /// A version 2 checkout snapshot on the wire. <paramref name="total"/> defaults to the price the
    /// server holds the items to, 999 cents each.
    /// </summary>
    protected static string CheckoutWire(
        string state,
        IReadOnlyList<string> items,
        long? total = null,
        string? receipt = null
    ) =>
        Wire(
            Checkout,
            2,
            state,
            new JsonObject
            {
                ["items"] = new JsonArray(items.Select(i => (JsonNode?)i).ToArray()),
                ["receipt"] = receipt,
                ["total"] = total ?? items.Count * (long)CheckoutMachine.UnitPriceCents,
            }
        );

    protected static string Wire(string machine, int version, string state, JsonObject context) =>
        new JsonObject
        {
            ["machine"] = machine,
            ["version"] = version,
            ["state"] = state,
            ["context"] = context,
        }.ToJsonString();

    /// <summary>A checkout draft at Review with <paramref name="items"/>, saved as Alice.</summary>
    protected async Task<Guid> CheckoutAtReview(params string[] items)
    {
        var id = Guid.NewGuid();
        var saved = await Machines.SaveAsync(Alice, Checkout, id, CheckoutWire("Review", items));
        saved.State.Should().Be("Review");
        return id;
    }
}
