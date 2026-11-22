using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Broadcaster.RabbitMQ;
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

/// <summary>
/// <c>UseRabbitMq(configuration)</c> reads the broker from configuration: a full connection string
/// as before, or the host, user and password as settings of their own, so a password is never
/// encoded into a URI.
/// </summary>
[TestFixture]
public class RabbitMqBrokerSettingsTests
{
    private const string Password = "a/b;c@d:e";

    private static RabbitMqBroadcasterOptions Registered(
        IConfiguration configuration,
        Action<RabbitMqBroadcasterOptions>? configure = null,
        string name = "RabbitMq"
    )
    {
        var services = new ServiceCollection();
        new TraxBuilder(services, new EffectRegistry()).AddEffects(effects =>
            effects.UseBroadcaster(b =>
                b.UseRabbitMq(
                    configuration,
                    o =>
                    {
                        o.SigningKey = TestBroadcastKey.Value;
                        o.AllowInsecureTransport = true;
                        configure?.Invoke(o);
                    },
                    name
                )
            )
        );
        return services.BuildServiceProvider().GetRequiredService<RabbitMqBroadcasterOptions>();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => KeyValuePair.Create(v.Key, (string?)v.Value)))
            .Build();

    [Test]
    public void The_broker_is_read_from_separate_settings()
    {
        var options = Registered(
            Configuration(
                ("RabbitMq:Host", "rabbitmq"),
                ("RabbitMq:UserName", "orders"),
                ("RabbitMq:Password", Password)
            )
        );

        options.ConnectionString.Should().Be("amqp://rabbitmq:5672/");
        var factory = options.CreateConnectionFactory();
        factory.HostName.Should().Be("rabbitmq");
        factory.Port.Should().Be(5672);
        factory.UserName.Should().Be("orders");
        factory.Password.Should().Be(Password);
    }

    [Test]
    public void The_settings_can_be_under_another_name_and_port()
    {
        var factory = Registered(
                Configuration(
                    ("Broker:Host", "rabbitmq"),
                    ("Broker:Port", "5673"),
                    ("Broker:UserName", "orders"),
                    ("Broker:Password", Password)
                ),
                name: "Broker"
            )
            .CreateConnectionFactory();

        factory.Port.Should().Be(5673);
        factory.Password.Should().Be(Password);
    }

    [Test]
    public void A_full_connection_string_still_connects_as_it_says()
    {
        var factory = Registered(
                Configuration(("ConnectionStrings:RabbitMq", "amqp://guest:guest@localhost:5672"))
            )
            .CreateConnectionFactory();

        factory.HostName.Should().Be("localhost");
        factory.UserName.Should().Be("guest");
        factory.Password.Should().Be("guest");
    }

    [Test]
    public void A_password_set_apart_takes_the_place_of_the_one_in_the_URI()
    {
        var factory = Registered(
                Configuration(
                    ("ConnectionStrings:RabbitMq", "amqps://guest:guest@broker.example.com"),
                    ("RabbitMq:UserName", "orders"),
                    ("RabbitMq:Password", Password)
                )
            )
            .CreateConnectionFactory();

        factory.HostName.Should().Be("broker.example.com");
        factory.UserName.Should().Be("orders");
        factory.Password.Should().Be(Password);
    }

    [Test]
    public void No_broker_in_configuration_is_refused()
    {
        var act = () => Registered(Configuration());

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*ConnectionStrings:RabbitMq*RabbitMq:Host*");
    }

    [Test]
    public void A_port_that_is_not_a_port_is_refused()
    {
        var act = () =>
            Registered(Configuration(("RabbitMq:Host", "rabbitmq"), ("RabbitMq:Port", "x")));

        act.Should().Throw<ArgumentException>().WithMessage("*RabbitMq:Port*not a TCP port*");
    }

    [Test]
    public void A_broker_on_another_host_set_apart_still_needs_TLS_or_an_opt_out()
    {
        var act = () =>
            Registered(
                Configuration(("RabbitMq:Host", "rabbitmq")),
                o => o.AllowInsecureTransport = false
            );

        act.Should().Throw<ArgumentException>().WithMessage("*not reached over TLS*");
    }
}
