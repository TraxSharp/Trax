using AwesomeAssertions;

namespace Trax.Core.Tests.Integration.Examples.Failures;

// Shown on https://traxsharp.net/docs/sdk-reference/trains-and-junctions/train

public class RunEitherTests
{
    [Test]
    public async Task RunEither_ChargeCardThrows_ReturnsTheExceptionInsteadOfThrowing()
    {
        var result = await new CheckoutTrain(new Outbox()).RunEither(new Order(20m));

        result.IsLeft.Should().BeTrue();
        var message = result.Match(Right: receipt => "", Left: failure => failure.Message);
        message.Should().Be("Card declined");
    }
}
