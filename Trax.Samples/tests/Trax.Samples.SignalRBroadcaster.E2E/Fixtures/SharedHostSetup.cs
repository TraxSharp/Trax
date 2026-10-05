using Trax.Samples.SignalRBroadcaster.E2E.Factories;

namespace Trax.Samples.SignalRBroadcaster.E2E.HubTests;

[SetUpFixture]
public class SharedHostSetup
{
    public static BroadcasterFactory Factory { get; private set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Factory = new BroadcasterFactory();
        _ = Factory.Server;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await Factory.DisposeAsync();
}
