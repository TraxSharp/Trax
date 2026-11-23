using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Trax.Effect.Broadcaster.RabbitMQ;

/// <summary>
/// Reads a broker from configuration: <c>ConnectionStrings:{name}</c>, or <c>{name}:Host</c> and
/// <c>{name}:Port</c>, with <c>{name}:UserName</c> and <c>{name}:Password</c> beside either.
/// </summary>
internal static class RabbitMqSettings
{
    public const int DefaultPort = 5672;

    public static (string ConnectionString, string? UserName, string? Password) Read(
        IConfiguration configuration,
        string name
    )
    {
        var section = configuration.GetSection(name);
        var connectionString = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var host = section["Host"];
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException(
                    $"UseRabbitMq(configuration) found no broker: set ConnectionStrings:{name} to an "
                        + $"AMQP URI, or {name}:Host (with {name}:UserName and {name}:Password).",
                    nameof(configuration)
                );
            var port = DefaultPort;
            if (
                section["Port"] is { Length: > 0 } text
                && (
                    !int.TryParse(
                        text,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out port
                    ) || port is < 1 or > 65535
                )
            )
                throw new ArgumentException(
                    $"{name}:Port is '{text}', which is not a TCP port.",
                    nameof(configuration)
                );
            connectionString = new UriBuilder("amqp", host.Trim(), port).Uri.ToString();
        }
        return (
            connectionString,
            NullIfEmpty(section["UserName"]),
            NullIfEmpty(section["Password"])
        );
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
