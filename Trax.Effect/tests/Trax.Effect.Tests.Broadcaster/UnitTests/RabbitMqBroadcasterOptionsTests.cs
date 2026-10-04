using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Broadcaster.RabbitMQ;
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;

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

    [Test]
    public void The_junction_exchange_defaults_to_the_train_exchange_with_a_suffix()
    {
        var options = new RabbitMqBroadcasterOptions
        {
            ConnectionString = "amqp://localhost",
            ExchangeName = "custom.exchange",
        };

        options.EffectiveJunctionExchangeName.Should().Be("custom.exchange.junctions");
    }

    [Test]
    public void UseRabbitMq_refuses_a_junction_exchange_that_is_the_train_exchange()
    {
        var act = () =>
            new TraxBuilder(new ServiceCollection(), new EffectRegistry()).AddEffects(effects =>
                effects.UseBroadcaster(b =>
                    b.UseRabbitMq("amqp://localhost", o => o.JunctionExchangeName = o.ExchangeName)
                )
            );

        act.Should().Throw<ArgumentException>().WithMessage("*JunctionExchangeName to differ*");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UseRabbitMq_binds_the_junction_exchange_only_on_a_host_that_handles_junction_events(
        bool handles
    )
    {
        var services = new ServiceCollection();
        if (handles)
            services.AddSingleton<Trax.Effect.Services.TrainEventBroadcaster.IJunctionEventHandler>(
                NSubstitute.Substitute.For<Trax.Effect.Services.TrainEventBroadcaster.IJunctionEventHandler>()
            );
        new TraxBuilder(services, new EffectRegistry()).AddEffects(effects =>
            effects.UseBroadcaster(b => b.UseRabbitMq("amqp://localhost"))
        );
        await using var provider = services.BuildServiceProvider();

        provider
            .GetRequiredService<RabbitMqTrainEventReceiver>()
            .BindJunctionExchange.Should()
            .Be(handles);
    }
}
