using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.BroadcasterBuilder;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.RabbitMQ.Extensions;

/// <summary>
/// Adds <c>UseRabbitMq</c> to the broadcaster builder, which makes RabbitMQ the transport that
/// carries train lifecycle events between processes.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/configuration/use-rabbit-mq">SDK reference</seealso>
public static class RabbitMqBroadcasterExtensions
{
    /// <summary>
    /// Configures RabbitMQ as the transport for cross-process lifecycle event broadcasting.
    /// </summary>
    /// <param name="builder">The broadcaster builder.</param>
    /// <param name="connectionString">AMQP connection URI (e.g., "amqp://guest:guest@localhost:5672").</param>
    /// <param name="configure">Optional callback to customize RabbitMQ options.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// Registers the options, a <see cref="RabbitMqTrainEventBroadcaster"/> as the singleton
    /// <see cref="ITrainEventBroadcaster"/>, and a <see cref="RabbitMqTrainEventReceiver"/> as the
    /// singleton <see cref="ITrainEventReceiver"/>. No connection is opened here: the broadcaster
    /// connects when it first has an event to send and the receiver when it is started. Publishing
    /// only queues the event, so an unreachable broker never holds up a train: the broadcaster's
    /// background sender retries, and drops events once 1024 are waiting.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="configure"/> set <see cref="RabbitMqBroadcasterOptions.PrefetchCount"/> to 0,
    /// which RabbitMQ reads as "no limit"; or it set no
    /// <see cref="RabbitMqBroadcasterOptions.SigningKey"/> and did not call
    /// <see cref="RabbitMqBroadcasterOptions.AllowUnsignedMessages"/>, or set a key shorter than
    /// <see cref="RabbitMqBroadcasterOptions.MinimumSigningKeyLength"/>; or
    /// <paramref name="connectionString"/> is plain <c>amqp://</c> to a host that is not loopback
    /// and <see cref="RabbitMqBroadcasterOptions.AllowInsecureTransport"/> is not set.
    /// </exception>
    public static BroadcasterBuilder UseRabbitMq(
        this BroadcasterBuilder builder,
        string connectionString,
        Action<RabbitMqBroadcasterOptions>? configure = null
    ) =>
        builder.UseRabbitMq(
            new RabbitMqBroadcasterOptions { ConnectionString = connectionString },
            configure
        );

    /// <summary>
    /// Configures RabbitMQ as the transport for cross-process lifecycle event broadcasting, with the
    /// broker read from configuration under <paramref name="name"/>.
    /// </summary>
    /// <param name="builder">The broadcaster builder.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="configure">Optional callback to customize RabbitMQ options.</param>
    /// <param name="name">The name the settings are under. Defaults to <c>RabbitMq</c>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// The broker is <c>ConnectionStrings:{name}</c>, an AMQP URI, when it is set; otherwise
    /// <c>{name}:Host</c>, with <c>{name}:Port</c> (default 5672), reached over plain
    /// <c>amqp://</c>. <c>{name}:UserName</c> and <c>{name}:Password</c>, when set, take the place of
    /// any user and password in the URI, so a password never has to be encoded into one. A host
    /// passing a full URI keeps working: a deployment that sets the parts separately sets no
    /// connection string. Everything else, and every refusal, is as for
    /// <see cref="UseRabbitMq(BroadcasterBuilder, string, Action{RabbitMqBroadcasterOptions}?)"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Neither <c>ConnectionStrings:{name}</c> nor <c>{name}:Host</c> is set, <c>{name}:Port</c> is
    /// not a port, or the options are refused as by the overload taking a connection string.
    /// </exception>
    public static BroadcasterBuilder UseRabbitMq(
        this BroadcasterBuilder builder,
        IConfiguration configuration,
        Action<RabbitMqBroadcasterOptions>? configure = null,
        string name = "RabbitMq"
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var (connectionString, userName, password) = RabbitMqSettings.Read(configuration, name);
        return builder.UseRabbitMq(
            new RabbitMqBroadcasterOptions
            {
                ConnectionString = connectionString,
                UserName = userName,
                Password = password,
            },
            configure
        );
    }

    private static BroadcasterBuilder UseRabbitMq(
        this BroadcasterBuilder builder,
        RabbitMqBroadcasterOptions options,
        Action<RabbitMqBroadcasterOptions>? configure
    )
    {
        configure?.Invoke(options);

        if (options.PrefetchCount == 0)
        {
            throw new ArgumentException(
                "UseRabbitMq() requires RabbitMqBroadcasterOptions.PrefetchCount of at least 1. "
                    + "A prefetch count of 0 means no limit, which lets unacknowledged events pile up "
                    + "in the receiving process. Omit the setting to use the default of 64.",
                nameof(configure)
            );
        }

        if (options.EffectiveJunctionExchangeName == options.ExchangeName)
        {
            throw new ArgumentException(
                "UseRabbitMq() requires RabbitMqBroadcasterOptions.JunctionExchangeName to differ "
                    + "from ExchangeName, so a receiver that predates junction events never receives "
                    + "one. Omit it to use the default, ExchangeName + \".junctions\".",
                nameof(configure)
            );
        }

        if (options.Problem() is { } problem)
            throw new ArgumentException($"UseRabbitMq(): {problem}", nameof(configure));

        builder.ServiceCollection.AddSingleton(options);
        Trax.Effect.Configuration.Deploy.DeployFact.Add(
            builder.ServiceCollection,
            Trax.Effect.Configuration.Deploy.DeployFact.Transport,
            "rabbitmq"
        );
        builder
            .ServiceCollection.AddSingleton<RabbitMqTrainEventBroadcaster>()
            .AddSingleton<ITrainEventBroadcaster>(sp =>
                sp.GetRequiredService<RabbitMqTrainEventBroadcaster>()
            );
        builder
            .ServiceCollection.AddSingleton(sp => new RabbitMqTrainEventReceiver(
                sp.GetRequiredService<RabbitMqBroadcasterOptions>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<RabbitMqTrainEventReceiver>>()
            )
            {
                // Only a host that handles junction events binds their exchange.
                BindJunctionExchange =
                    sp.GetService<IServiceProviderIsService>()
                        ?.IsService(typeof(IJunctionEventHandler))
                    ?? true,
            })
            .AddSingleton<ITrainEventReceiver>(sp =>
                sp.GetRequiredService<RabbitMqTrainEventReceiver>()
            );

        return builder;
    }
}
