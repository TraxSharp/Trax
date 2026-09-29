using FluentAssertions;
using Trax.Effect.Broadcaster.RabbitMQ;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

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

    [Test]
    public void DefaultPrefetchCount_IsBounded()
    {
        var options = new RabbitMqBroadcasterOptions { ConnectionString = "amqp://localhost" };

        options.PrefetchCount.Should().Be(64);
    }

    [Test]
    public async Task Receiver_PrefetchCountZero_RefusesToStartBeforeConnecting()
    {
        // The URI points nowhere: the refusal must come before any connection attempt.
        await using var receiver = new RabbitMqTrainEventReceiver(
            new RabbitMqBroadcasterOptions
            {
                ConnectionString = "amqp://trax:trax123@127.0.0.1:1/",
                PrefetchCount = 0,
            }
        );

        var act = () => receiver.StartAsync((_, _) => Task.CompletedTask, CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*PrefetchCount must be at least 1*");
    }
}
