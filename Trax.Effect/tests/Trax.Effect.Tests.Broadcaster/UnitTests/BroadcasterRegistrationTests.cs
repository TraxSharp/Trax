using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Broadcaster.RabbitMQ;
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

[TestFixture]
public class BroadcasterRegistrationTests
{
    [Test]
    public void UseBroadcaster_RegistersBroadcastLifecycleHookFactory()
    {
        var services = new ServiceCollection();
        var registry = new EffectRegistry();
        var builder = new TraxBuilder(services, registry);

        builder.AddEffects(effects =>
            effects.UseBroadcaster(b => b.UseRabbitMq("amqp://localhost"))
        );

        var hookFactories = services
            .Where(sd => sd.ServiceType == typeof(ITrainLifecycleHookFactory))
            .ToList();

        hookFactories.Should().NotBeEmpty();
    }

    [Test]
    public void UseBroadcaster_RegistersTrainEventReceiverService()
    {
        var services = new ServiceCollection();
        var registry = new EffectRegistry();
        var builder = new TraxBuilder(services, registry);

        builder.AddEffects(effects =>
            effects.UseBroadcaster(b => b.UseRabbitMq("amqp://localhost"))
        );

        var hostedServices = services
            .Where(sd => sd.ServiceType == typeof(IHostedService))
            .ToList();

        hostedServices
            .Should()
            .Contain(sd => sd.ImplementationType == typeof(TrainEventReceiverService));
    }

    [Test]
    public void UseRabbitMq_RegistersBroadcasterAndReceiver()
    {
        var services = new ServiceCollection();
        var registry = new EffectRegistry();
        var builder = new TraxBuilder(services, registry);

        builder.AddEffects(effects =>
            effects.UseBroadcaster(b => b.UseRabbitMq("amqp://localhost"))
        );

        services.Should().Contain(sd => sd.ServiceType == typeof(ITrainEventBroadcaster));
        services.Should().Contain(sd => sd.ServiceType == typeof(ITrainEventReceiver));
    }

    [Test]
    public void UseRabbitMq_RegistersOptions()
    {
        var services = new ServiceCollection();
        var registry = new EffectRegistry();
        var builder = new TraxBuilder(services, registry);

        builder.AddEffects(effects =>
            effects.UseBroadcaster(b => b.UseRabbitMq("amqp://trax:trax123@localhost:5672"))
        );

        services.Should().Contain(sd => sd.ServiceType == typeof(RabbitMqBroadcasterOptions));
    }

    [Test]
    public void UseRabbitMq_WithConfigureCallback_CustomizesOptions()
    {
        var services = new ServiceCollection();
        var registry = new EffectRegistry();
        var builder = new TraxBuilder(services, registry);

        builder.AddEffects(effects =>
            effects.UseBroadcaster(b =>
                b.UseRabbitMq("amqp://localhost", opts => opts.ExchangeName = "custom.exchange")
            )
        );

        // Build and resolve to verify the options were customized
        services.AddLogging();
        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<RabbitMqBroadcasterOptions>();

        options.ExchangeName.Should().Be("custom.exchange");
        options.ConnectionString.Should().Be("amqp://localhost");
    }

    [Test]
    public void UseBroadcaster_RegistersHookAsNonToggleable()
    {
        var services = new ServiceCollection();
        var registry = new EffectRegistry();
        var builder = new TraxBuilder(services, registry);

        builder.AddEffects(effects =>
            effects.UseBroadcaster(b => b.UseRabbitMq("amqp://localhost"))
        );

        // The broadcast hook should be registered and enabled (non-toggleable)
        registry.IsEnabled(typeof(LifecycleHookFactory<BroadcastLifecycleHook>)).Should().BeTrue();
    }

    [Test]
    public void UseBroadcaster_ChainsWithOtherEffectMethods()
    {
        var services = new ServiceCollection();
        var registry = new EffectRegistry();
        var builder = new TraxBuilder(services, registry);

        // Verify it returns the builder for chaining
        var act = () =>
            builder.AddEffects(effects =>
                effects
                    .UseBroadcaster(b => b.UseRabbitMq("amqp://localhost"))
                    .SetEffectLogLevel(Microsoft.Extensions.Logging.LogLevel.Trace)
            );

        act.Should().NotThrow();
    }
}
