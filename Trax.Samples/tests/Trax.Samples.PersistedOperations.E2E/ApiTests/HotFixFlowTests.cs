using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Samples.PersistedOperations.E2E.Fixtures;

namespace Trax.Samples.PersistedOperations.E2E.ApiTests;

/// <summary>
/// The sample's three stories, driven the way its client drives them: the operator uploads with
/// the demo key, the app calls by id with no credential.
/// </summary>
[TestFixture]
[Category("E2E")]
public class HotFixFlowTests : ApiTestBase
{
    [Test]
    public async Task ShippedApp_CallsById_AndAnInlineQueryIsRefused()
    {
        await UploadAsync("greet_v1", GreetDocuments.Casual);

        using var byId = await AppCallsAsync("greet_v1", "Alice");
        GreetDocuments.GreetingOf(byId).Should().Be("Hey Alice!");

        using var app = SharedApiSetup.Factory.CreateClient();
        var inline = await app.PostAsJsonAsync(
            "/trax/graphql/",
            new { query = GreetDocuments.Inline }
        );
        ((int)inline.StatusCode).Should().Be(400);
        (await inline.Content.ReadAsStringAsync()).Should().Contain("PERSISTED_OPERATION_REQUIRED");
    }

    [Test]
    public async Task HotFix_ToFormal_ChangesTheAnswerWithoutChangingTheCall()
    {
        await UploadAsync("greet_v1", GreetDocuments.Casual);
        using (var before = await AppCallsAsync("greet_v1", "Alice"))
            GreetDocuments.GreetingOf(before).Should().Be("Hey Alice!");

        // Same response shape, so the guardrail accepts it with no bypass.
        await UploadAsync("greet_v1", GreetDocuments.Formal);

        using var after = await AppCallsAsync("greet_v1", "Alice");
        GreetDocuments.GreetingOf(after).Should().Be("Good day, Alice.");
    }

    [Test]
    public async Task RenamingTheFieldTheAppReads_IsRefused_AndTheOldAnswerStands()
    {
        await UploadAsync("greet_v1", GreetDocuments.Casual);
        await UploadAsync("greet_v1", GreetDocuments.Formal);

        using var refused = await UploadRawAsync("greet_v1", GreetDocuments.RenamedField);
        var payload = UploadPayload(refused);
        payload.GetProperty("success").GetBoolean().Should().BeFalse(payload.GetRawText());
        payload
            .GetProperty("errors")[0]
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("SHAPE_DIFF_VIOLATION");

        (await Store.GetAsync("greet_v1", null, CancellationToken.None))!
            .Document.Should()
            .Be(GreetDocuments.Formal);
        using var after = await AppCallsAsync("greet_v1", "Alice");
        GreetDocuments.GreetingOf(after).Should().Be("Good day, Alice.");
    }

    [Test]
    public async Task ReUploadingTheManifest_RestoresCasual()
    {
        // The client's first step on every run: back from FORMAL to CASUAL, same shape.
        await UploadAsync("greet_v1", GreetDocuments.Formal);
        await UploadAsync("greet_v1", GreetDocuments.Casual);

        using var doc = await AppCallsAsync("greet_v1", "Alice");
        GreetDocuments.GreetingOf(doc).Should().Be("Hey Alice!");
    }

    [Test]
    public async Task PersistedOperationHistory_AccumulatesEveryMutationInOrder()
    {
        // Drive the storage through Upsert → Upsert (rewrite) → Deactivate → Restore, then
        // assert the history table holds exactly four rows in that ChangeType order.
        const string id = "history_check_v1";
        await Store.UpsertAsync(id, GreetDocuments.Casual, null, CancellationToken.None);
        await Store.UpsertAsync(id, GreetDocuments.Formal, null, CancellationToken.None);
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
        await Store.UpsertAsync(
            "lifecycle_v1",
            GreetDocuments.Casual,
            null,
            CancellationToken.None
        );
        await Store.DeactivateAsync("lifecycle_v1", null, "test", CancellationToken.None);

        var resp = await PostAsync(
            new { id = "lifecycle_v1", variables = GreetDocuments.Variables("X") }
        );
        var body = await resp.Content.ReadAsStringAsync();
        GraphQLErrors.FirstCode(body).Should().Be(GraphQLErrors.UnknownPersistedOperation, body);

        await Store.RestoreAsync("lifecycle_v1", null, CancellationToken.None);

        using var doc = await PostJsonAsync(
            new { id = "lifecycle_v1", variables = GreetDocuments.Variables("Y") }
        );
        GreetDocuments.GreetingOf(doc).Should().Be("Hey Y!");
    }
}
