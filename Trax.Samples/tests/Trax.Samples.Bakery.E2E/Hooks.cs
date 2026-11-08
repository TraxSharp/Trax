using Reqnroll;
using Trax.Samples.Bakery.Api.Auth;
using Trax.Samples.Bakery.E2E.Fixtures;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Gherkin.Reqnroll;

namespace Trax.Samples.Bakery.E2E;

/// <summary>
/// Starts the Bakery host once and points the Trax feature bindings at it: every person of every scenario signs in
/// with a token of their own, so no scenario sees another's orders or shifts.
/// </summary>
[Binding]
public sealed class Hooks
{
    private static BakeryApiFactory? s_factory;

    [BeforeTestRun]
    public static void Start()
    {
        s_factory = new BakeryApiFactory();
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
            o.Credential = (actor, person, scenario) =>
            {
                var subject = $"{person}-{Math.Abs(scenario.GetHashCode()):x8}";
                return new FeatureCredential(
                    new Dictionary<string, string>
                    {
                        ["Authorization"] = "Bearer " + DemoTokens.Mint(subject, actor.Roles[0]),
                    }
                );
            };
        });
    }

    /// <summary>
    /// A scenario tagged <c>@in-memory</c> shows what an outcome the host's own train never produces by itself does
    /// (a payment that fails, or is called off). It runs in memory (<see cref="InMemoryRunTests"/>), not here.
    /// </summary>
    [BeforeScenario("in-memory")]
    public static void InMemoryOnly() =>
        Assert.Ignore(
            "Runs in memory only: the host's payment train neither fails nor is called off by itself."
        );

    [AfterTestRun]
    public static async Task Stop()
    {
        if (s_factory is not null)
            await s_factory.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }
}
