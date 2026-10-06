using Trax.Samples.ContentShield.E2E.Factories;
using Trax.Samples.ContentShield.E2E.Fixtures;

namespace Trax.Samples.ContentShield.E2E.ApiTests;

[SetUpFixture]
public class SharedApiSetup
{
    public static ContentShieldApiFactory Factory { get; private set; } = null!;

    internal static TestRunner Runner { get; private set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Runner = TestRunner.Start(TestPostgres.WithPort(ContentShieldApiFactory.ConnectionString));

        // The API migrates the database on startup; the runner builds its services on the
        // first request it receives, which comes after.
        Factory = new ContentShieldApiFactory(Runner.BaseUrl);
        _ = Factory.Services;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        try
        {
            await Factory.DisposeAsync();
        }
        catch (RabbitMQ.Client.Exceptions.AlreadyClosedException) { }

        Npgsql.NpgsqlConnection.ClearAllPools();
    }
}
