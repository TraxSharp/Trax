using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Samples.PersistedOperations.E2E.Fixtures;
using Trax.Samples.PersistedOperations.Models;

namespace Trax.Samples.PersistedOperations.E2E.ApiTests;

/// <summary>
/// Regression coverage for the bug where any <c>UpsertAsync</c> call against a
/// schema carrying <c>@authorize</c> crashed with
/// <c>HotChocolate.MissingStateException</c>. The fix in
/// <c>HotChocolateSchemaValidator</c> seeds the authorization handler into
/// the validator's context data so the standalone validator path works the
/// same as the request-pipeline path that runs at execution time.
/// <para>
/// The sample sample's <c>UserNote</c> query model carries
/// <c>[TraxAuthorize(Roles = "user")]</c>, which flips on HotChocolate's
/// <c>@authorize</c> directive in the schema. Without the fix every test in
/// the E2E suite would crash at the first <c>UpsertAsync</c>; this file
/// pins the specific scenario down.
/// </para>
/// </summary>
[TestFixture]
[Category("E2E")]
public class AuthorizedSchemaUpsertTests : ApiTestBase
{
    private const string GatedNotesQuery =
        "query UserNotes { discover { notes { userNotes(first: 5) { totalCount } } } }";

    [Test]
    public async Task Upsert_OperationTargetingGatedField_Succeeds()
    {
        var op = await Store.UpsertAsync(
            "auth_notes_v1",
            GatedNotesQuery,
            options: null,
            CancellationToken.None
        );

        op.Id.Should().Be("auth_notes_v1");
        op.Document.Should().Be(GatedNotesQuery);

        // Round-trip read confirms the row landed.
        var row = await Store.GetAsync("auth_notes_v1", null, CancellationToken.None);
        row.Should().NotBeNull();
        row!.Document.Should().Be(GatedNotesQuery);
    }

    [Test]
    public async Task Upsert_AnyOperation_SucceedsAgainstAuthorizeAugmentedSchema()
    {
        // The bug fired on every UpsertAsync regardless of whether the
        // operation targeted a gated field, because the validator runs the
        // authorize-rule aggregator unconditionally whenever @authorize is in
        // the schema. This case exercises the non-gated greet field to prove
        // the fix isn't field-specific.
        const string ungatedDoc =
            "query Greet($input: GreetInput!) { discover { greeting { greet(input: $input) { greeting } } } }";

        var op = await Store.UpsertAsync(
            "auth_greet_v1",
            ungatedDoc,
            options: null,
            CancellationToken.None
        );

        op.Document.Should().Be(ungatedDoc);
    }

    [Test]
    public async Task UploadMutation_OperationTargetingGatedField_ReportsSuccess()
    {
        // Same as Upsert_OperationTargetingGatedField_Succeeds but driven
        // through the GraphQL admin mutation, which is the path dashboards
        // and the sample client use. Exercises the full middleware /
        // mutation pipeline end-to-end.
        using var json = await PostJsonAsync(
            new
            {
                query = """
                mutation Up($input: UploadPersistedOperationInput!) {
                  operations {
                    persistedOperations {
                      uploadPersistedOperation(input: $input) {
                        success
                        errors { code message }
                      }
                    }
                  }
                }
                """,
                variables = new
                {
                    input = new { id = "auth_mut_notes_v1", document = GatedNotesQuery },
                },
            }
        );

        var payload = json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("uploadPersistedOperation");
        payload.GetProperty("success").GetBoolean().Should().BeTrue(payload.GetRawText());
        payload.GetProperty("errors").GetArrayLength().Should().Be(0);

        var stored = await Store.GetAsync("auth_mut_notes_v1", null, CancellationToken.None);
        stored.Should().NotBeNull();
    }

    [Test]
    public async Task UserNote_RoundTripThroughEf_PreservesEveryColumn()
    {
        // The model's properties are only meaningful when they survive an EF
        // round-trip against the real Postgres schema created by the API
        // bootstrap. This case writes one row through the DbContext factory
        // (the same registration the GraphQL query model resolves through),
        // reads it back via LINQ, and asserts every column made it.
        var factory = SharedApiSetup.Factory!.Services.GetRequiredService<
            IDbContextFactory<UserNotesDbContext>
        >();

        await using (var write = await factory.CreateDbContextAsync())
        {
            await write.Database.ExecuteSqlRawAsync(
                "TRUNCATE TABLE notes.user_notes RESTART IDENTITY;"
            );

            var created = new DateTime(2026, 5, 21, 12, 30, 45, DateTimeKind.Utc);
            write.Notes.Add(
                new UserNote
                {
                    Title = "first note",
                    Body = "hello, gated world",
                    CreatedAt = created,
                }
            );
            await write.SaveChangesAsync();
        }

        await using var read = await factory.CreateDbContextAsync();
        var row = await read.Notes.AsNoTracking().SingleAsync();

        row.Id.Should().BeGreaterThan(0);
        row.Title.Should().Be("first note");
        row.Body.Should().Be("hello, gated world");
        row.CreatedAt.Should()
            .Be(new DateTime(2026, 5, 21, 12, 30, 45, DateTimeKind.Utc).ToUniversalTime());
    }

    [Test]
    public async Task PersistedExecution_OfGatedQuery_WithoutAuth_ReturnsAuthorizationError()
    {
        // Belt-and-braces: confirms the schema really does carry @authorize.
        // If a future change accidentally drops the directive (or registers
        // the model as anonymous), the upsert tests above would still pass
        // but this one would start returning data instead of an auth error,
        // catching the regression.
        await Store.UpsertAsync(
            "auth_exec_notes_v1",
            GatedNotesQuery,
            options: null,
            CancellationToken.None
        );

        using var doc = await PostJsonAsync(new { id = "auth_exec_notes_v1" });

        doc.RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeTrue("the gated field must reject unauthenticated callers");
        errors.GetArrayLength().Should().BeGreaterThan(0);
        var firstError = errors[0];
        firstError.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
        firstError
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("TRAX_AUTHORIZATION");
    }
}
