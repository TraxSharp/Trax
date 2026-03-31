using Trax.Samples.ChatService.E2E.Factories;

namespace Trax.Samples.ChatService.E2E.ChatApiTests;

[SetUpFixture]
public class SharedChatApiSetup
{
    public static ChatServiceApiFactory Factory { get; private set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Factory = new ChatServiceApiFactory();
        // Accessing Services triggers host startup (runs chat schema migration).
        _ = Factory.Services;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Factory.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }
}
