using Reqnroll;
using Trax.Samples.CheckIn.Api.Auth;
using Trax.Samples.CheckIn.E2E.Fixtures;
using Trax.Samples.Shared.Testing;
using Trax.StateMachine;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Gherkin.Reqnroll;

namespace Trax.Samples.CheckIn.E2E;

/// <summary>
/// Starts the CheckIn host once and points the Trax feature bindings at it: every person of every scenario signs in
/// with a token of their own, so no scenario sees another's bookings or desks, and every request goes by the persisted
/// id the page's client manifest holds, since the host runs no other.
/// </summary>
[Binding]
public sealed class Hooks
{
    private static CheckInApiFactory? s_factory;

    [BeforeTestRun]
    public static void Start()
    {
        s_factory = new CheckInApiFactory();
        var client = s_factory.CreateClient();
        TraxFeatures.Configure(o =>
        {
            o.Host = s_factory.Services;
            o.Client = client;
            o.Glossary = FeatureGlossary.Read(
                File.ReadAllText(
                    Path.Combine(AppContext.BaseDirectory, "features", "glossary.json")
                )
            );
            o.Manifest = ClientManifest.Read(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "client", "manifest.json"))
            );
            o.Credential = DemoFeatureCredentials.PerPerson(DemoTokens.Issuer);
        });
    }

    /// <summary>
    /// A scenario tagged <c>@in-memory</c> shows what an outcome the host's own train never produces by itself does
    /// (a boarding pass that cannot be issued, or is called off). It runs in memory (<see cref="InMemoryRunTests"/>), not here.
    /// </summary>
    [BeforeScenario("in-memory")]
    public static void InMemoryOnly() =>
        Assert.Ignore(
            "Runs in memory only: the host's boarding pass train neither fails nor is called off by itself."
        );

    [AfterTestRun]
    public static async Task Stop()
    {
        if (s_factory is not null)
            await s_factory.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }
}
