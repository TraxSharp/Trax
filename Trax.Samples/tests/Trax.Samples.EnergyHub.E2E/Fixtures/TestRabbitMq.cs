namespace Trax.Samples.EnergyHub.E2E.Fixtures;

/// <summary>
/// The broker the hub and worker test hosts connect to. It defaults to the user docker-compose.yml
/// and CI create (<c>trax</c> / <c>trax123</c>) on localhost:5672; <c>TRAX_TEST_RABBITMQ</c>
/// overrides the whole URI for a machine whose broker lives elsewhere.
/// </summary>
internal static class TestRabbitMq
{
    private const string Default = "amqp://trax:trax123@localhost:5672";

    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("TRAX_TEST_RABBITMQ") is { Length: > 0 } uri
            ? uri
            : Default;
}
