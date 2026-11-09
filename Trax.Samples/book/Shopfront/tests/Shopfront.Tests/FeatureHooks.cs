using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Reqnroll;
using Shopfront.Auth;
using Shopfront.Data;
using Trax.StateMachine;
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

        hub = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseEnvironment("Development").UseSetting("ConnectionStrings:Shopfront", Database)
        );
        var client = hub.CreateClient();
        var features = Path.Combine(AppContext.BaseDirectory, "features");
        // The hub runs only the documents it registered, so the bindings send each step by the
        // persisted id the storefront's client manifest holds for it.
        var manifest = Path.Combine(AppContext.BaseDirectory, "manifest.json");

        // The lexicon is read with the trains' records, as the hub reads them.
        var lexicon = Lexicon.ReadDirectory(
            features,
            [typeof(Shopfront.Trains.ReturnOrder.IReturnOrderTrain).Assembly]
        )!;

        TraxFeatures.Configure(trax =>
        {
            trax.Host = hub.Services;
            trax.Client = client;
            trax.Glossary = lexicon.Glossary!;
            trax.Manifest = ClientManifest.Read(File.ReadAllText(manifest));
            trax.Credential = (actor, person, scenario) =>
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
