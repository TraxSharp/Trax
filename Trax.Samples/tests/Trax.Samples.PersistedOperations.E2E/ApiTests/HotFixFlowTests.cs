using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Samples.PersistedOperations.E2E.Fixtures;

namespace Trax.Samples.PersistedOperations.E2E.ApiTests;

[TestFixture]
[Category("E2E")]
public class HotFixFlowTests : ApiTestBase
{
    private const string GreetDoc =
        "query Greet($input: GreetInput!) { discover { greeting { greet(input: $input) { greeting } } } }";

    private const string LookupUserDoc =
        "query LookupUser($input: LookupUserInput!) { discover { users { lookupUser(input: $input) { userId displayName email loginCount } } } }";

    [Test]
    public async Task DistinctIds_DispatchToDistinctTrains()
    {
        await Store.UpsertAsync("greet_distinct_v1", GreetDoc, null, CancellationToken.None);
        await Store.UpsertAsync(
            "lookupUser_distinct_v1",
            LookupUserDoc,
            null,
            CancellationToken.None
        );

        using var greet = await PostJsonAsync(
            new { id = "greet_distinct_v1", variables = new { input = new { name = "Alice" } } }
        );
        greet
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("greeting")
            .GetProperty("greet")
            .GetProperty("greeting")
            .GetString()
            .Should()
            .Be("Hello, Alice.");

        using var lookup = await PostJsonAsync(
            new
            {
                id = "lookupUser_distinct_v1",
                variables = new { input = new { userId = "user-1" } },
            }
        );
        lookup
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("users")
            .GetProperty("lookupUser")
            .GetProperty("displayName")
            .GetString()
            .Should()
            .Be("User user-1");
    }

    [Test]
    public async Task PersistedOperation_WithMultipleVariableInputs_DispatchesEachCorrectly()
    {
        await Store.UpsertAsync("greet_multi_v1", GreetDoc, null, CancellationToken.None);

        foreach (var name in new[] { "Alice", "Bob", "Charlie", "Dana" })
        {
            using var doc = await PostJsonAsync(
                new { id = "greet_multi_v1", variables = new { input = new { name } } }
            );
            doc.RootElement.GetProperty("data")
                .GetProperty("discover")
                .GetProperty("greeting")
                .GetProperty("greet")
                .GetProperty("greeting")
                .GetString()
                .Should()
                .Be($"Hello, {name}.");
        }
    }

    [Test]
    public async Task PersistedOperation_LookupUser_ReturnsTrainResolverOutput()
    {
        await Store.UpsertAsync("lookup_v1", LookupUserDoc, null, CancellationToken.None);

        using var doc = await PostJsonAsync(
            new { id = "lookup_v1", variables = new { input = new { userId = "user-99" } } }
        );

        var user = doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("users")
            .GetProperty("lookupUser");

        user.GetProperty("userId").GetString().Should().Be("user-99");
        user.GetProperty("displayName").GetString().Should().Be("User user-99");
        user.GetProperty("email").GetString().Should().Be("user-99@example.test");
        user.GetProperty("loginCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task PersistedOperationHistory_AccumulatesEveryMutationInOrder()
    {
        // Drive the storage through Upsert → Upsert (rewrite) → Deactivate →
        // Restore, then assert the history table contains exactly four rows
        // in the right ChangeType order. The previous version of this test
        // queried ListAsync (live rows) and only confirmed the live row
        // existed — it would have passed even if no history rows had been
        // written.
        const string id = "history_check_v1";
        await Store.UpsertAsync(id, GreetDoc, null, CancellationToken.None);
        await Store.UpsertAsync(id, GreetDoc + " # rewrite", null, CancellationToken.None);
        await Store.DeactivateAsync(id, null, "test cleanup", CancellationToken.None);
        await Store.RestoreAsync(id, null, CancellationToken.None);

        var factory =
            SharedApiSetup.Factory!.Services.GetRequiredService<IDataContextProviderFactory>();
        var ctx = await factory.CreateDbContextAsync(CancellationToken.None);

        var rows = await ctx
            .PersistedOperationHistories.Where(h => h.Id == id)
            .OrderBy(h => h.ChangedAt)
            .ThenBy(h => h.HistoryId)
            .Select(h => h.ChangeType)
            .ToListAsync();

        rows.Should().Equal("Upsert", "Upsert", "Deactivate", "Restore");

        var deactivateRow = await ctx.PersistedOperationHistories.FirstAsync(h =>
            h.Id == id && h.ChangeType == "Deactivate"
        );
        deactivateRow
            .ChangedReason.Should()
            .Be("test cleanup", "Deactivate must record the operator-supplied reason");
    }

    [Test]
    public async Task DeactivateThenRestore_DispatchResumesAfterRestore()
    {
        await Store.UpsertAsync("lifecycle_v1", GreetDoc, null, CancellationToken.None);
        await Store.DeactivateAsync("lifecycle_v1", null, "test", CancellationToken.None);

        var resp = await PostAsync(
            new { id = "lifecycle_v1", variables = new { input = new { name = "X" } } }
        );
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("errors");

        await Store.RestoreAsync("lifecycle_v1", null, CancellationToken.None);

        using var doc = await PostJsonAsync(
            new { id = "lifecycle_v1", variables = new { input = new { name = "Y" } } }
        );
        doc.RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("greeting")
            .GetProperty("greet")
            .GetProperty("greeting")
            .GetString()
            .Should()
            .Be("Hello, Y.");
    }
}
