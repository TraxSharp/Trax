using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class TakePaymentJunctionTests
{
    private sealed class ScriptedPaymentProvider(Exception? failWith = null) : IPaymentProvider
    {
        public List<(
            string CustomerId,
            string PaymentToken,
            decimal Amount,
            string? Key
        )> Charges { get; } = [];

        public Task<ChargeResult> Charge(
            string customerId,
            string paymentToken,
            decimal amount,
            string? idempotencyKey,
            CancellationToken cancellationToken
        )
        {
            Charges.Add((customerId, paymentToken, amount, idempotencyKey));
            return failWith is null
                ? Task.FromResult(
                    new ChargeResult("tx_test", "https://pay.example/receipts/tx_test")
                )
                : Task.FromException<ChargeResult>(failWith);
        }
    }

    private static readonly Customer Ada = new("TraxJwt:ada");

    private static readonly PlaceOrderInput Order = new()
    {
        Lines = [new OrderLine("TEE", 3)],
        PaymentToken = "tok_visa",
    };

    private static readonly PricedCart Cart = new([new PricedLine("TEE", 3, 25.00m)], 75.00m);

    // Resets the database and reserves the three T-shirts, as ReserveStockJunction leaves them.
    [SetUp]
    public async Task ReserveTheTShirts()
    {
        await TestDatabase.Reset();
        await using var db = TestDatabase.NewContext();
        (await new Catalogue(db).TryReserve([("TEE", 3)], CancellationToken.None))
            .Should()
            .BeNull();
    }

    private static async Task<Payment> Run(IPaymentProvider payments)
    {
        await using var db = TestDatabase.NewContext();
        await using var services = new ServiceCollection().BuildServiceProvider();
        var junction = new TakePaymentJunction(
            payments,
            new Catalogue(db),
            services,
            NullLogger<TakePaymentJunction>.Instance
        );
        return await junction.Run((Ada, Order, Cart, new StockReservation(Guid.NewGuid())));
    }

    [Test]
    public async Task Run_NotInvokedByACheckout_ChargesWithoutAKey()
    {
        var payments = new ScriptedPaymentProvider();

        var payment = await Run(payments);

        payments.Charges.Should().Equal(("TraxJwt:ada", "tok_visa", 75.00m, (string?)null));
        payment
            .Should()
            .Be(new Payment("tx_test", 75.00m, "https://pay.example/receipts/tx_test"));
        (await TestDatabase.StockOf("TEE")).Should().Be(0);
    }

    [Test]
    public async Task Run_Declined_ThrowsTheReasonAndReleasesTheStock()
    {
        var act = () =>
            Run(
                new ScriptedPaymentProvider(new PaymentDeclinedException("The card was declined."))
            );

        await act.Should().ThrowAsync<TrainException>().WithMessage("The card was declined.");
        (await TestDatabase.StockOf("TEE")).Should().Be(3, "the three T-shirts are back");
    }

    [Test]
    public async Task Run_TimedOut_RethrowsAndReleasesTheStock()
    {
        var act = () => Run(new ScriptedPaymentProvider(new TimeoutException("No answer.")));

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        (await TestDatabase.StockOf("TEE")).Should().Be(3, "the three T-shirts are back");
    }

    [Test]
    public async Task FakeProvider_SameKey_AnswersTheFirstChargeAgain()
    {
        var provider = new FakePaymentProvider();

        var first = await provider.Charge(
            "TraxJwt:ada",
            "tok_visa",
            75.00m,
            "checkout:1",
            CancellationToken.None
        );
        var again = await provider.Charge(
            "TraxJwt:ada",
            "tok_visa",
            75.00m,
            "checkout:1",
            CancellationToken.None
        );

        again.Should().Be(first, "a retry with the same key is not charged again");
    }
}
