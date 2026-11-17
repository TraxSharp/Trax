namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// Broadcast every persisted-operation change over RabbitMQ, so each node empties its
    /// persisted-operation caches when any node uploads, deactivates or restores an operation.
    /// </summary>
    /// <remarks>
    /// Every node caches the documents it serves: HotChocolate's parsed-document and
    /// prepared-operation caches always, and the Trax lookup cache when
    /// <see cref="WithInMemoryCache"/> is on, each for at most <see cref="WithCacheMaxAge"/>. A
    /// change made on one node reaches the others at once only through this broadcast; the
    /// maximum age is the backstop when a broadcast is lost. A publish the broker does not confirm
    /// is reported to the caller as <c>CHANGE_NOT_BROADCAST</c>. Each node binds its own
    /// queue to a fanout exchange; when the connection to the broker drops, the node empties its
    /// caches, and empties them again once the connection recovers, because a change broadcast
    /// in between was not delivered to it.
    /// <para>
    /// A host that runs more than one node needs this; a host that runs one declares
    /// <see cref="SingleNode"/> instead. Persisted operations refuse to start with neither.
    /// </para>
    /// <para>
    /// Every message is signed with the key <paramref name="configure"/> sets
    /// (<see cref="RabbitMqInvalidationOptions.SigningKey"/>), and a node drops one that is not.
    /// Persisted operations refuse to start without a key unless
    /// <see cref="RabbitMqInvalidationOptions.AllowUnsignedMessages"/> is called, and refuse plain
    /// <c>amqp://</c> to a host that is not loopback unless
    /// <see cref="RabbitMqInvalidationOptions.AllowInsecureTransport"/> is set.
    /// </para>
    /// </remarks>
    /// <param name="connectionString">The broker every node and uploader uses.</param>
    /// <param name="configure">Sets the signing key, and any opt-out.</param>
    public PersistedOperationsBuilder UseRabbitMqInvalidation(
        string connectionString,
        Action<RabbitMqInvalidationOptions> configure
    )
    {
        ArgumentNullException.ThrowIfNull(configure);
        UseRabbitMqInvalidation(connectionString);
        configure(_rabbitMqInvalidation);
        return this;
    }

    /// <summary>
    /// <see cref="UseRabbitMqInvalidation(string, Action{RabbitMqInvalidationOptions})"/>, with the
    /// broker read from configuration under <paramref name="name"/>: <c>ConnectionStrings:{name}</c>,
    /// an AMQP URI, when it is set; otherwise <c>{name}:Host</c>, with <c>{name}:Port</c> (default
    /// 5672), over plain <c>amqp://</c>. <c>{name}:UserName</c> and <c>{name}:Password</c>, when
    /// set, take the place of any user and password in the URI, so a password never has to be
    /// encoded into one. These are the settings <c>UseRabbitMq(configuration)</c> reads for the
    /// train-event broadcaster.
    /// </summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="configure">Sets the signing key, and any opt-out.</param>
    /// <param name="name">The name the settings are under. Defaults to <c>RabbitMq</c>.</param>
    /// <exception cref="ArgumentException">
    /// Neither <c>ConnectionStrings:{name}</c> nor <c>{name}:Host</c> is set, or
    /// <c>{name}:Port</c> is not a port.
    /// </exception>
    public PersistedOperationsBuilder UseRabbitMqInvalidation(
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        Action<RabbitMqInvalidationOptions> configure,
        string name = "RabbitMq"
    )
    {
        ArgumentNullException.ThrowIfNull(configure);
        UseRabbitMqInvalidation(RabbitMqSettings.Read(configuration, name, _rabbitMqInvalidation));
        configure(_rabbitMqInvalidation);
        return this;
    }

    // The store's registration hands over the options its own builder collected.
    internal PersistedOperationsBuilder UseStoreRabbitMqInvalidation(
        string connectionString,
        RabbitMqInvalidationOptions invalidation
    )
    {
        UseRabbitMqInvalidation(connectionString);
        _rabbitMqInvalidation = invalidation;
        return this;
    }

    /// <summary>
    /// Broadcast every persisted-operation change over RabbitMQ, with no signing key yet. Persisted
    /// operations refuse to start until one is set through
    /// <see cref="UseRabbitMqInvalidation(string, Action{RabbitMqInvalidationOptions})"/>.
    /// </summary>
    public PersistedOperationsBuilder UseRabbitMqInvalidation(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException(
                "UseRabbitMqInvalidation requires a connection string.",
                nameof(connectionString)
            );

        _rabbitMqConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Declare that exactly one process serves this endpoint and writes the persisted-operation
    /// store, so a change made through it is seen by every cache there is.
    /// </summary>
    /// <remarks>
    /// Persisted operations refuse to start unless the host either declares this or calls
    /// <see cref="UseRabbitMqInvalidation(string, Action{RabbitMqInvalidationOptions})"/>. The declaration is a claim about the deployment that
    /// nothing at runtime can check: run a second node, or write the store from another process
    /// (a CI uploader using <c>AddPersistedOperationStore</c>), and a change made there reaches
    /// this node's caches only when the entry reaches its maximum age (<see cref="WithCacheMaxAge"/>).
    /// Such a deployment needs the broadcaster.
    /// </remarks>
    public PersistedOperationsBuilder SingleNode()
    {
        _singleNode = true;
        return this;
    }
}
