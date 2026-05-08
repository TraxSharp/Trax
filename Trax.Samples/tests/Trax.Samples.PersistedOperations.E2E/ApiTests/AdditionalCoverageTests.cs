using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Samples.PersistedOperations.E2E.Fixtures;

namespace Trax.Samples.PersistedOperations.E2E.ApiTests;

[TestFixture]
[Category("E2E")]
public class AdditionalCoverageTests : ApiTestBase
{
    private const string GreetDoc =
        "query Greet($input: GreetInput!) { discover { greeting { greet(input: $input) { greeting } } } }";

    // ----- Deactivated-id error shape -----

    [Test]
    public async Task DeactivatedId_ReturnsErrorsArrayWithCode()
    {
        await Store.UpsertAsync("deactivated_v1", GreetDoc, null, CancellationToken.None);
        await Store.DeactivateAsync("deactivated_v1", null, "test", CancellationToken.None);

        using var doc = await PostJsonAsync(
            new { id = "deactivated_v1", variables = new { input = new { name = "x" } } }
        );

        doc.RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeTrue("deactivated id should produce a GraphQL errors array");
        errors.GetArrayLength().Should().BeGreaterThan(0);
        errors[0].TryGetProperty("message", out _).Should().BeTrue();
    }

    // ----- Tenant scoping isolation (storage layer) -----

    [Test]
    public async Task TenantScoping_DocumentsAreIsolatedByTenantKey()
    {
        await Store.UpsertAsync(
            "tenant_scope_v1",
            GreetDoc,
            new UpsertOptions { TenantKey = "tenant-a" },
            CancellationToken.None
        );
        await Store.UpsertAsync(
            "tenant_scope_v1",
            GreetDoc + " # tenant-b variant",
            new UpsertOptions { TenantKey = "tenant-b" },
            CancellationToken.None
        );

        var a = await Store.GetAsync("tenant_scope_v1", "tenant-a", CancellationToken.None);
        var b = await Store.GetAsync("tenant_scope_v1", "tenant-b", CancellationToken.None);
        var none = await Store.GetAsync("tenant_scope_v1", null, CancellationToken.None);

        a.Should().NotBeNull();
        b.Should().NotBeNull();
        a!.Document.Should().NotBe(b!.Document);
        none.Should().BeNull("the null-tenant row was never written for this id");
    }

    // ----- Auth ordering: storage isn't read pre-auth -----

    [Test]
    public async Task PersistedRequest_DispatchesToTrain_ReturningExpectedData()
    {
        // The sample has no authentication wired (the production wiring is
        // documented in the package README), so the strongest claim this
        // test can make about auth ordering is: the persisted-op middleware
        // and storage ARE consulted, and the request reaches the train. If
        // the middleware were broken or the storage path bypassed, the
        // response would not match the train's deterministic output.
        await Store.UpsertAsync("auth_smoke_v1", GreetDoc, null, CancellationToken.None);

        using var doc = await PostJsonAsync(
            new { id = "auth_smoke_v1", variables = new { input = new { name = "anon" } } }
        );

        // Strong assertion: the response must contain the train's output,
        // not just be "any GraphQL envelope". A broken pipeline would leave
        // either an errors array OR a missing data field.
        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
        doc.RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("greeting")
            .GetProperty("greet")
            .GetProperty("greeting")
            .GetString()
            .Should()
            .Be("Hello, anon.");
    }

    // ----- Content-type variants -----

    [Test]
    public async Task ContentTypeWithCharset_AcceptedAndProducesCorrectTrainOutput()
    {
        await Store.UpsertAsync("content_v1", GreetDoc, null, CancellationToken.None);

        var json = "{\"id\":\"content_v1\",\"variables\":{\"input\":{\"name\":\"Charset\"}}}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.ContentType!.CharSet = "utf-8";
        var resp = await Http.PostAsync("/trax/graphql/", content);
        ((int)resp.StatusCode).Should().Be(200);

        var body = await resp.Content.ReadAsStringAsync();
        body.Should()
            .Contain(
                "\"greeting\":\"Hello, Charset.\"",
                "the request must reach the train and the train output must round-trip"
            );
    }

    [Test]
    public async Task EmptyJsonObject_RejectedByHotChocolateNotByMiddleware()
    {
        var json = "{}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await Http.PostAsync("/trax/graphql/", content);
        var body = await resp.Content.ReadAsStringAsync();
        ((int)resp.StatusCode).Should().Be(400);
        body.Should().NotContain("PERSISTED_OPERATION_REQUIRED");
    }

    // ----- Batched requests through real HC -----

    [Test]
    public async Task BatchedRequest_AllPersisted_PassesThroughMiddlewareToHC()
    {
        // HC v15's standard `/graphql` endpoint does not execute JSON-array
        // batches; it returns HC0009 "Invalid GraphQL Request". The behavior
        // under our control is the persisted-operations middleware: when
        // every entry has a persisted id (no inline `query`), the middleware
        // must NOT reject and must NOT short-circuit — it must pass the body
        // through to HC. We assert that here by checking HC's parser error
        // is reached, not the middleware's rejection error.
        await Store.UpsertAsync("batch_a_v1", GreetDoc, null, CancellationToken.None);
        await Store.UpsertAsync("batch_b_v1", GreetDoc, null, CancellationToken.None);

        var json =
            "[{\"id\":\"batch_a_v1\",\"variables\":{\"input\":{\"name\":\"Anna\"}}},"
            + "{\"id\":\"batch_b_v1\",\"variables\":{\"input\":{\"name\":\"Bob\"}}}]";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await Http.PostAsync("/trax/graphql/", content);
        var body = await resp.Content.ReadAsStringAsync();

        body.Should()
            .NotContain(
                "PERSISTED_OPERATION_REQUIRED",
                "the middleware must not reject batches whose entries are all persisted"
            );
        // HC's parser error code (HC0009) means the request reached HC's
        // parser — i.e. our middleware passed it through. If the request had
        // been rejected by the middleware, the body would carry our typed
        // PERSISTED_OPERATION_REQUIRED code instead.
        body.Should().Contain("HC0009", "request must reach HC, not be short-circuited by us");
    }

    [Test]
    public async Task BatchedRequest_OneInlineQuery_RejectsWholeBatch()
    {
        await Store.UpsertAsync("batch_persisted_v1", GreetDoc, null, CancellationToken.None);

        var json =
            "[{\"id\":\"batch_persisted_v1\"},"
            + "{\"query\":\"{ discover { greeting { greet(input: { name: \\\"y\\\" }) { greeting } } } }\"}]";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await Http.PostAsync("/trax/graphql/", content);
        ((int)resp.StatusCode).Should().Be(400);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("PERSISTED_OPERATION_REQUIRED");
    }

    // ----- Multiple inputs proves correct execution -----

    [Test]
    public async Task SamePersistedOperation_DifferentVariables_DispatchesEachToTrain()
    {
        await Store.UpsertAsync("multivar_v1", GreetDoc, null, CancellationToken.None);

        foreach (var name in new[] { "Anna", "Beth", "Carl", "Diane" })
        {
            using var doc = await PostJsonAsync(
                new { id = "multivar_v1", variables = new { input = new { name } } }
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

    // ----- Shape-diff guardrail (E2E sanity) -----

    [Test]
    public async Task ShapeChangingEdit_IsRejectedAtTheStorageLayer()
    {
        await Store.UpsertAsync("shape_e2e_v1", GreetDoc, null, CancellationToken.None);

        // Add greetedAt → response shape changes.
        var changedShape =
            "query Greet($input: GreetInput!) { discover { greeting { greet(input: $input) { greeting greetedAt } } } }";

        Func<Task> act = () =>
            Store.UpsertAsync("shape_e2e_v1", changedShape, null, CancellationToken.None);

        await act.Should().ThrowAsync<ShapeDiffViolationException>();
    }
}
