using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.StateMachine.Persistence;
using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Recovery.E2E.Utilities;
using Trax.Samples.Recovery.Machines;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The <c>order</c> machine, a user's own food order, driven over GraphQL: fill the cart, place the order, and the
/// <c>DeliverOrderTrain</c> run its <c>Ordered</c> state invokes decides where it ends. The demo's trouble (a tablet
/// that times out, a closed restaurant) is armed for the next order with <c>dispatch { orderTrouble }</c>.
/// </summary>
[TestFixture]
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class OrderMachineTests : RecoveryTestFixture
{
    private OrderDraft _order = null!;

    [SetUp]
    public void NewOrder() => _order = new OrderDraft(GraphQL, OperatorKey);

    [TearDown]
    public Task NoTroubleLeftArmed() => ArmAsync("NONE");

    [Test]
    public void TheMachine_Builds_AndItsIrRoutesEveryOutcomeAndTheCartsTimer()
    {
        var ir = JsonNode.Parse(new OrderMachine().ExportIr())!.AsObject();

        ir["id"]!.GetValue<string>().Should().Be(OrderMachine.MachineId);
        var outcomes = ir["outcomes"]!.AsObject();
        Target(outcomes, "Ordered.done").Should().Be(nameof(OrderState.Delivered));
        Target(outcomes, "Ordered.failed").Should().Be(nameof(OrderState.Cancelled));
        Target(outcomes, "Ordered.cancelled").Should().Be(nameof(OrderState.Cancelled));

        var timer = ir["timers"]!.AsArray().Should().ContainSingle().Subject!;
        timer["state"]!.GetValue<string>().Should().Be(nameof(OrderState.Cart));
        timer["target"]!.GetValue<string>().Should().Be(nameof(OrderState.Cleared));
        timer["after"]!
            .GetValue<string>()
            .Should()
            .Be("PT30M", "a cart waits 30 minutes as shipped");
    }

    [Test]
    public async Task AnOrder_IsDelivered_AndTheServerPricesTheCart()
    {
        await _order.StartAsync();
        var added = await _order.AddItemAsync("Margherita");
        added = await _order.AddItemAsync("Garlic bread");

        added.State.Should().Be(nameof(OrderState.Cart));
        added.Context.GetProperty("total").GetDecimal().Should().Be(17m, "the menu's prices");
        (await _order.PlaceOrderAsync()).State.Should().Be(nameof(OrderState.Ordered));

        await _order.WaitForStateAsync(nameof(OrderState.Delivered));
        Names(await StepsAsync())
            .Should()
            .Equal("SendToRestaurant", "ChargeCard", "Cook", "PickUp", "Deliver");
    }

    [Test]
    public async Task PlaceOrderPressedTwice_MakesOneOrderAndOneRun()
    {
        await _order.StartAsync();
        await _order.AddItemAsync("Margherita");

        // A double click: the same request sent twice, the second before the first answered.
        var press = $"place-{Guid.NewGuid():N}";
        var answers = await Task.WhenAll(
            _order.PlaceOrderAsync(press),
            // allowed-delay: the stagger only orders the two sends; no result is awaited by time.
            Task.Delay(50).ContinueWith(_ => _order.PlaceOrderAsync(press)).Unwrap()
        );
        // A second press under a request of its own.
        var again = await _order.PlaceOrderAsync();

        answers[0].State.Should().Be(nameof(OrderState.Ordered));
        answers[1]
            .Revision.Should()
            .Be(answers[0].Revision, "one request sent twice is answered once");
        again
            .Problem.Should()
            .Be("no-transition", "an order already placed cannot be placed again");
        await _order.WaitForStateAsync(nameof(OrderState.Delivered));
        (await RunsAsync()).Should().ContainSingle("one order queues one run");
    }

    [Test]
    public async Task ATabletThatTimesOut_IsTriedAgain_AndTheOrderIsDelivered()
    {
        await ArmAsync("TABLET_TIMES_OUT");
        await _order.StartAsync();
        await _order.AddItemAsync("Margherita");
        await _order.PlaceOrderAsync();

        await _order.WaitForStateAsync(nameof(OrderState.Delivered));

        var calls = (await TabletCallsAsync()).TakeLast(2).ToList();
        calls.Select(c => c.GetProperty("attempt").GetInt32()).Should().Equal(1, 2);
        calls[0].GetProperty("problem").GetString().Should().Contain("did not answer");
        calls[1].GetProperty("problem").ValueKind.Should().Be(JsonValueKind.Null);
        (await RunsAsync()).Should().ContainSingle("the retry is inside the one run");
    }

    [Test]
    public async Task AClosedRestaurant_CancelsTheOrder_BeforeTheCardIsCharged()
    {
        await ArmAsync("RESTAURANT_CLOSED");
        await _order.StartAsync();
        await _order.AddItemAsync("Margherita");
        await _order.PlaceOrderAsync();

        await _order.WaitForStateAsync(nameof(OrderState.Cancelled));

        var steps = await StepsAsync();
        Names(steps).Should().Equal("SendToRestaurant");
        steps[0].GetProperty("state").GetString().Should().Be("FAILED");
    }

    [Test]
    public async Task ACartNobodyOrders_IsClearedByTheServer()
    {
        await _order.StartAsync();
        var cart = await _order.AddItemAsync("Pepperoni");
        cart.Answer.GetProperty("dueAt").GetString().Should().NotBeNull("the cart's timer runs");

        // The wait passes: move its due time into the past and let the server sweep it.
        await using (var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope())
            await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .SnapshotDrafts.Where(d => d.Id == _order.Id && d.Machine == OrderMachine.MachineId)
                .ExecuteUpdateAsync(x =>
                    x.SetProperty(d => d.DueAt, DateTimeOffset.UtcNow.AddMinutes(-1))
                );
        await SharedRecoverySetup
            .Factory.Services.GetRequiredService<InvokeOutcomeReconciler>()
            .FireDueTimersOnce(CancellationToken.None);

        (await _order.LoadAsync()).State.Should().Be(nameof(OrderState.Cleared));
        (await _order.PlaceOrderAsync())
            .Problem.Should()
            .Be("no-transition", "a cleared cart cannot be ordered");
    }

    [Test]
    public async Task AnEmptyCart_OrAnItemNotOnTheMenu_IsRefused()
    {
        await _order.StartAsync();

        (await _order.PlaceOrderAsync()).Problem.Should().Be("guard-failed");
        (await _order.AddItemAsync("Sushi")).Problem.Should().Be("guard-failed");
        (await _order.LoadAsync()).State.Should().Be(nameof(OrderState.Cart));
    }

    private static string Target(JsonObject outcomes, string trigger) =>
        outcomes[trigger]!["edges"]![0]!["to"]!.GetValue<string>();

    private async Task ArmAsync(string trouble)
    {
        var response = await GraphQL.SendAsync(
            "mutation($t: OrderTrouble!) { dispatch { orderTrouble(input: { trouble: $t }) { output { trouble } } } }",
            OperatorKey,
            new { t = trouble }
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
    }

    private async Task<List<long>> RunsAsync()
    {
        await using var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .Metadatas.Where(m =>
                m.InvokingMachine == OrderMachine.MachineId && m.InvokingInstanceId == _order.Id
            )
            .Select(m => m.Id)
            .ToListAsync();
    }

    private async Task<List<JsonElement>> StepsAsync()
    {
        var run = (await RunsAsync()).Should().ContainSingle().Subject;
        var response = await GraphQL.SendAsync(
            "query($id: Long!) { operations { junctionRuns(metadataId: $id) { position name state } } }",
            OperatorKey,
            new { id = run }
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        return response
            .GetData("operations", "junctionRuns")
            .EnumerateArray()
            .OrderBy(s => s.GetProperty("position").GetInt32())
            .Select(s => s.Clone())
            .ToList();
    }

    private async Task<List<JsonElement>> TabletCallsAsync()
    {
        var response = await GraphQL.SendAsync(
            "{ discover { orders { tabletCalls { calls { orderId attempt problem } } } } }",
            OperatorKey
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        return response
            .GetData("discover", "orders", "tabletCalls", "calls")
            .EnumerateArray()
            .Select(c => c.Clone())
            .ToList();
    }
}
