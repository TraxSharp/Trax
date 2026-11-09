using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Reqnroll;
using Shopfront.Auth;
using Shopfront.Data;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Gherkin.Reqnroll;

namespace Shopfront.Tests;

// Starts the hub once for the features and points the Trax bindings at it. Every person of every
// scenario signs in as a user of their own, so no scenario sees another's returns.
[Binding]
public sealed class FeatureHooks
{
    // A database of their own, new for each run: the other tests drop theirs before each test,
    // which would pull it out from under a running hub.
    private const string Database =
        "Host=localhost;Database=shopfront_features;Username=postgres;Password=shopfront";

    private static WebApplicationFactory<Program>? hub;

    [BeforeTestRun]
    public static async Task StartTheHub()
    {
        await using (var db = Context())
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
        }

        // The bindings write their documents as text, which the hub refuses unless the
        // Development-only switch says otherwise, as DocumentsAsText sets it for the host tests.
        hub = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseEnvironment("Development")
                .UseSetting("ConnectionStrings:Shopfront", Database)
                .UseSetting("PersistedOperations:Require", "false")
        );
        var client = hub.CreateClient();
        var glossary = Path.Combine(AppContext.BaseDirectory, "features", "glossary.json");

        TraxFeatures.Configure(features =>
        {
            features.Host = hub.Services;
            features.Client = client;
            features.Glossary = FeatureGlossary.Read(File.ReadAllText(glossary));
            features.Credential = (actor, person, scenario) =>
                new FeatureCredential(
                    new Dictionary<string, string>
                    {
                        ["Authorization"] =
                            "Bearer " + DevJwt.Mint($"{person}-{scenario}", actor.Roles[0]),
                    }
                );
        });
    }

    // The hub hands every job to the worker, and these tests start none: a scenario that waits for
    // the check's answer runs in memory only (ReturnFeaturesTests).
    [BeforeScenario("in-memory")]
    public static void InMemoryOnly() =>
        Assert.Ignore("Runs in memory only: no worker answers the check here.");

    [AfterTestRun]
    public static async Task StopTheHub()
    {
        if (hub is not null)
            await hub.DisposeAsync();
    }

    private static ShopfrontDbContext Context() =>
        new(
            new DbContextOptionsBuilder<ShopfrontDbContext>()
                .UseNpgsql(Database, ShopfrontDatabase.Npgsql)
                .Options,
            TestCaller.Staff
        );
}
