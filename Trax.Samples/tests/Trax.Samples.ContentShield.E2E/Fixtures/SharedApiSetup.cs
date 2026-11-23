using Trax.Core.Extensions;
using Trax.Samples.ContentShield.E2E.Factories;
using Trax.Samples.ContentShield.E2E.Fixtures;

namespace Trax.Samples.ContentShield.E2E.ApiTests;

[SetUpFixture]
public class SharedApiSetup
{
    public static ContentShieldApiFactory Factory
    {
        get => field.NotNull(nameof(Factory));
        private set;
    }

    internal static TestRunner Runner
    {
        get => field.NotNull(nameof(Runner));
        private set;
    }

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
