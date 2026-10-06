using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Samples.Auth.Auth;
using Trax.Samples.Auth.Data;
using Trax.Samples.Auth.E2E.Utilities;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Auth.E2E.Fixtures;

/// <summary>
/// Base for the tests against the shared Development host: a GraphQL client, every demo user as
/// an API-key caller and as a JWT caller, and a reader for the audit trail the host writes.
/// </summary>
public abstract class AuthTestFixture
{
    protected HttpClient Http { get; private set; } = null!;

    protected GraphQLClient GraphQL { get; private set; } = null!;

    protected static Caller Anonymous => Caller.Anonymous;

    protected static Caller AliceKey => Caller.WithApiKey("alice (key)", DemoCredentials.AliceKey);

    protected static Caller ErinKey => Caller.WithApiKey("erin (key)", DemoCredentials.ErinKey);

    protected static Caller BobKey => Caller.WithApiKey("bob (key)", DemoCredentials.BobKey);

    protected static Caller OscarKey => Caller.WithApiKey("oscar (key)", DemoCredentials.OscarKey);

    protected Caller AliceToken { get; private set; } = null!;

    protected Caller ErinToken { get; private set; } = null!;

    protected Caller BobToken { get; private set; } = null!;

    protected Caller OscarToken { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task CreateClients()
    {
        Http = SharedAuthSetup.Factory.CreateClient();
        GraphQL = new GraphQLClient(Http);

        // Tokens come from the host's own Development-only endpoint, the way a reader gets one.
        AliceToken = Caller.WithToken("alice (jwt)", await MintAsync("alice"));
        ErinToken = Caller.WithToken("erin (jwt)", await MintAsync("erin"));
        BobToken = Caller.WithToken("bob (jwt)", await MintAsync("bob"));
        OscarToken = Caller.WithToken("oscar (jwt)", await MintAsync("oscar"));
    }

    [OneTimeTearDown]
    public void DisposeClients() => Http.Dispose();

    protected async Task<string> MintAsync(string user)
    {
        var minted = await Http.GetFromJsonAsync<MintedToken>($"/dev/token/{user}");
        return minted!.Token;
    }

    /// <summary>
    /// The audit rows for one operation, waiting for the background writer to flush them. Each
    /// test names its operation uniquely, so it reads only its own rows.
    /// </summary>
    protected static async Task<List<AuditRecord>> AuditFor(string operationName, int expected = 1)
    {
        List<AuditRecord> rows = [];
        var arrived = await Polling.WaitUntilAsync(
            async () =>
            {
                using var scope = SharedAuthSetup.Factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<INewsroomDbContext>();
                rows = await db
                    .AuditRecords.AsNoTracking()
                    .Where(r => r.OperationName == operationName)
                    .OrderBy(r => r.Id)
                    .ToListAsync();
                return rows.Count >= expected;
            },
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(100)
        );

        arrived
            .Should()
            .BeTrue($"{expected} audit row(s) for operation '{operationName}' should be written");
        return rows;
    }

    /// <summary>A GraphQL operation name no other test uses.</summary>
    protected static string UniqueOperation(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    protected static async Task<int> CountArticlesTitled(string title)
    {
        using var scope = SharedAuthSetup.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<INewsroomDbContext>();
        return await db.Articles.CountAsync(a => a.Title == title);
    }

    private sealed record MintedToken(string Token);
}
