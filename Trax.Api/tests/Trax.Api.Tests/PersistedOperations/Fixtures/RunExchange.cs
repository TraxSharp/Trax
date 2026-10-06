using RabbitMQ.Client;

namespace Trax.Api.Tests.PersistedOperations.Fixtures;

/// <summary>
/// An invalidation exchange of a fixture's own. Every run of the suite on one broker would
/// otherwise share the production exchange name, and one run's broadcasts would reach another
/// run's receivers.
/// </summary>
internal static class RunExchange
{
    /// <summary>A name no other fixture or run uses.</summary>
    public static string New() => $"trax.test.persisted_operations.{Guid.NewGuid():N}";

    /// <summary>Deletes <paramref name="exchange"/> from the broker, if it can be reached.</summary>
    public static async Task DeleteAsync(string amqpUri, string exchange)
    {
        try
        {
            var factory = new ConnectionFactory { Uri = new Uri(amqpUri) };
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            await channel.ExchangeDeleteAsync(exchange);
        }
        catch (Exception)
        {
            // The broker went away: there is nothing left to clean up.
        }
    }
}
