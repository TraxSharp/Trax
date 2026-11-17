using Microsoft.Extensions.Configuration;

namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

/// <summary>
/// Reads a broker from configuration: <c>ConnectionStrings:{name}</c>, or <c>{name}:Host</c> and
/// <c>{name}:Port</c>, with <c>{name}:UserName</c> and <c>{name}:Password</c> beside either. The
/// same settings <c>UseRabbitMq(configuration)</c> reads for the train-event broadcaster.
/// </summary>
internal static class RabbitMqSettings
{
    public const int DefaultPort = 5672;

    /// <summary>
    /// The broker's URI, which holds no credentials when they are set apart, and applies the user
    /// and password set apart to <paramref name="invalidation"/>.
    /// </summary>
    public static string Read(
        IConfiguration configuration,
        string name,
        RabbitMqInvalidationOptions invalidation
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(name);
        var connectionString = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var host = section["Host"];
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException(
                    $"UseRabbitMqInvalidation(configuration) found no broker: set "
                        + $"ConnectionStrings:{name} to an AMQP URI, or {name}:Host (with "
                        + $"{name}:UserName and {name}:Password).",
                    nameof(configuration)
                );
            var port = DefaultPort;
            if (
                section["Port"] is { Length: > 0 } text
                && (!int.TryParse(text, out port) || port is < 1 or > 65535)
            )
                throw new ArgumentException(
                    $"{name}:Port is '{text}', which is not a TCP port.",
                    nameof(configuration)
                );
            connectionString = new UriBuilder("amqp", host.Trim(), port).Uri.ToString();
        }
        if (section["UserName"] is { Length: > 0 } userName)
            invalidation.UserName = userName;
        if (section["Password"] is { Length: > 0 } password)
            invalidation.Password = password;
        return connectionString;
    }
}
