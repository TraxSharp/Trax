using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.Tests.PersistedOperations.Fixtures;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// <c>UseRabbitMqInvalidation(configuration, ...)</c> reads the broker from configuration: a full
/// connection string as before, or the host, user and password as settings of their own, so a
/// password is never encoded into a URI.
/// </summary>
[TestFixture]
public class InvalidationBrokerSettingsTests
{
    private const string Password = "a/b;c@d:e";

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => KeyValuePair.Create(v.Key, (string?)v.Value)))
            .Build();

    private static void Signed(RabbitMqInvalidationOptions o)
    {
        o.SigningKey = TestInvalidationKey.Value;
        o.AllowInsecureTransport = true;
    }

    [Test]
    public void The_broker_is_read_from_separate_settings()
    {
        var options = new PersistedOperationsBuilder()
            .UseRabbitMqInvalidation(
                Configuration(
                    ("RabbitMq:Host", "rabbitmq"),
                    ("RabbitMq:UserName", "orders"),
                    ("RabbitMq:Password", Password)
                ),
                Signed
            )
            .Build();

        options.RabbitMqConnectionString.Should().Be("amqp://rabbitmq:5672/");
        var factory = options.RabbitMqInvalidation.CreateConnectionFactory(
            options.RabbitMqConnectionString!
        );
        factory.HostName.Should().Be("rabbitmq");
        factory.Port.Should().Be(5672);
        factory.UserName.Should().Be("orders");
        factory.Password.Should().Be(Password);
    }

    [Test]
    public void The_store_reads_the_same_settings()
    {
        var store = new PersistedOperationStoreBuilder().UseRabbitMqInvalidation(
            Configuration(
                ("Broker:Host", "rabbitmq"),
                ("Broker:Port", "5673"),
                ("Broker:UserName", "orders"),
                ("Broker:Password", Password)
            ),
            Signed,
            "Broker"
        );

        var (connectionString, invalidation) = store.Build();

        connectionString.Should().Be("amqp://rabbitmq:5673/");
        invalidation.UserName.Should().Be("orders");
        invalidation.Password.Should().Be(Password);
    }

    [Test]
    public void A_full_connection_string_still_connects_as_it_says()
    {
        var options = new PersistedOperationsBuilder()
            .UseRabbitMqInvalidation(
                Configuration(("ConnectionStrings:RabbitMq", "amqp://guest:guest@localhost:5672")),
                Signed
            )
            .Build();

        var factory = options.RabbitMqInvalidation.CreateConnectionFactory(
            options.RabbitMqConnectionString!
        );
        factory.UserName.Should().Be("guest");
        factory.Password.Should().Be("guest");
    }

    [Test]
    public void A_password_set_apart_takes_the_place_of_the_one_in_the_URI()
    {
        var options = new PersistedOperationsBuilder()
            .UseRabbitMqInvalidation(
                Configuration(
                    ("ConnectionStrings:RabbitMq", "amqps://guest:guest@broker.example.com"),
                    ("RabbitMq:Password", Password)
                ),
                Signed
            )
            .Build();

        options
            .RabbitMqInvalidation.CreateConnectionFactory(options.RabbitMqConnectionString!)
            .Password.Should()
            .Be(Password);
    }

    [Test]
    public void No_broker_in_configuration_is_refused()
    {
        var act = () =>
            new PersistedOperationsBuilder().UseRabbitMqInvalidation(Configuration(), Signed);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*ConnectionStrings:RabbitMq*RabbitMq:Host*");
    }

    [Test]
    public void A_broker_on_another_host_set_apart_still_needs_TLS_or_an_opt_out()
    {
        var act = () =>
            new PersistedOperationsBuilder()
                .UseRabbitMqInvalidation(
                    Configuration(("RabbitMq:Host", "rabbitmq")),
                    o => o.SigningKey = TestInvalidationKey.Value
                )
                .Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*not reached over TLS*");
    }
}
