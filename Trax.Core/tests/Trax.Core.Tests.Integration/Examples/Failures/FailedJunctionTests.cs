using AwesomeAssertions;

namespace Trax.Core.Tests.Integration.Examples.Failures;

// Shown on https://traxsharp.net/docs/cross-cutting/handle-errors-in-trains

public class FailedJunctionTests
{
    [Test]
    public async Task Run_ChargeCardThrows_SendsNoReceiptAndThrowsTheSameException()
    {
        var outbox = new Outbox();

        var checkout = () => new CheckoutTrain(outbox).Run(new Order(20m));

        var declined = await checkout.Should().ThrowAsync<InvalidOperationException>();
        declined.Which.Message.Should().Be("Card declined");
        outbox.Sent.Should().BeEmpty();
    }
}
