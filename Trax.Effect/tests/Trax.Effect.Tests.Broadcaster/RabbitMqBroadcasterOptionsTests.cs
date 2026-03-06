using FluentAssertions;
using Trax.Effect.Broadcaster.RabbitMQ;

namespace Trax.Effect.Tests.Broadcaster;

[TestFixture]
public class RabbitMqBroadcasterOptionsTests
{
    [Test]
    public void DefaultExchangeName_IsTraxLifecycle()
    {
        var options = new RabbitMqBroadcasterOptions { ConnectionString = "amqp://localhost" };

        options.ExchangeName.Should().Be("trax.lifecycle");
    }

    [Test]
    public void ExchangeName_CanBeCustomized()
    {
        var options = new RabbitMqBroadcasterOptions
        {
            ConnectionString = "amqp://localhost",
            ExchangeName = "custom.exchange",
        };

        options.ExchangeName.Should().Be("custom.exchange");
    }
}
