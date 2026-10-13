using System.Net.Http.Json;
using System.Text;
using AwesomeAssertions;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Samples.PersistedOperations.E2E.Fixtures;

namespace Trax.Samples.PersistedOperations.E2E.ApiTests;

[TestFixture]
[Category("E2E")]
public class AdditionalCoverageTests : ApiTestBase
{
    // ----- Deactivated-id error shape -----

    [Test]
    public async Task DeactivatedId_ReturnsErrorsArrayWithCode()
    {
        await Store.UpsertAsync(
            "deactivated_v1",
            GreetDocuments.Casual,
            null,
            CancellationToken.None
        );
        await Store.DeactivateAsync("deactivated_v1", null, "test", CancellationToken.None);

        using var doc = await PostJsonAsync(
            new { id = "deactivated_v1", variables = GreetDocuments.Variables("x") }
        );

        doc.RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeTrue("deactivated id should produce a GraphQL errors array");
        errors.GetArrayLength().Should().BeGreaterThan(0);
        GraphQLErrors.FirstCode(doc).Should().Be(GraphQLErrors.UnknownPersistedOperation);
        errors[0]
            .GetProperty("extensions")
            .GetProperty("requestedKey")
            .GetString()
            .Should()
            .Be("deactivated_v1");
    }

    // ----- Tenant scoping isolation (storage layer) -----

    [Test]
    public async Task TenantScoping_DocumentsAreIsolatedByTenantKey()
    {
        await Store.UpsertAsync(
            "tenant_scope_v1",
            GreetDocuments.Casual,
            new UpsertOptions { TenantKey = "tenant-a" },
            CancellationToken.None
        );
        await Store.UpsertAsync(
            "tenant_scope_v1",
            GreetDocuments.Casual + " # tenant-b variant",
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

    // ----- Content-type variants -----

    [Test]
    public async Task ContentTypeWithCharset_AcceptedAndProducesCorrectTrainOutput()
    {
        await Store.UpsertAsync("content_v1", GreetDocuments.Casual, null, CancellationToken.None);

        var json = "{\"id\":\"content_v1\",\"variables\":{\"name\":\"Charset\"}}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.ContentType!.CharSet = "utf-8";
        var resp = await Http.PostAsync("/trax/graphql/", content);
        ((int)resp.StatusCode).Should().Be(200);

        var body = await resp.Content.ReadAsStringAsync();
        body.Should()
            .Contain(
                "\"greeting\":\"Hey Charset!\"",
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
    public async Task BatchedRequest_OfPersistedIds_IsRefusedByTheEndpoint()
    {
        // The Trax endpoint does not execute JSON-array batches: HotChocolate answers HC0009
        // "Invalid GraphQL Request" before enforcement runs. Enforcement lives inside the
        // execution pipeline now, so no batch reaches it, and none executes.
        await Store.UpsertAsync("batch_a_v1", GreetDocuments.Casual, null, CancellationToken.None);
        await Store.UpsertAsync("batch_b_v1", GreetDocuments.Casual, null, CancellationToken.None);

        var json =
            "[{\"id\":\"batch_a_v1\",\"variables\":{\"name\":\"Anna\"}},"
            + "{\"id\":\"batch_b_v1\",\"variables\":{\"name\":\"Bob\"}}]";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await Http.PostAsync("/trax/graphql/", content);
        var body = await resp.Content.ReadAsStringAsync();

        ((int)resp.StatusCode).Should().Be(400);
        body.Should().Contain("HC0009");
        body.Should().NotContain("Hey Anna!", "a refused batch runs none of its entries");
    }

    [Test]
    public async Task BatchedRequest_WithAnInlineQuery_RunsNothing()
    {
        // The point of enforcement: an inline document smuggled into a batch beside a persisted
        // id must not execute. Whichever layer refuses it, nothing in the batch runs.
        await Store.UpsertAsync(
            "batch_persisted_v1",
            GreetDocuments.Casual,
            null,
            CancellationToken.None
        );

        var json =
            "[{\"id\":\"batch_persisted_v1\",\"variables\":{\"name\":\"x\"}},"
            + "{\"query\":\"{ discover { greeting { greet(input: { name: \\\"y\\\", style: CASUAL }) { greeting } } } }\"}]";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await Http.PostAsync("/trax/graphql/", content);
        var body = await resp.Content.ReadAsStringAsync();

        ((int)resp.StatusCode).Should().Be(400);
        body.Should().NotContain("Hey y!", "the inline document in the batch must not run");
        body.Should().NotContain("Hey x!", "the batch is refused as a whole");
    }

    // ----- Multiple inputs proves correct execution -----

    [Test]
    public async Task SamePersistedOperation_DifferentVariables_DispatchesEachToTrain()
    {
        await Store.UpsertAsync("multivar_v1", GreetDocuments.Casual, null, CancellationToken.None);

        foreach (var name in new[] { "Anna", "Beth", "Carl", "Diane" })
        {
            using var doc = await PostJsonAsync(
                new { id = "multivar_v1", variables = GreetDocuments.Variables(name) }
            );
            GreetDocuments.GreetingOf(doc).Should().Be($"Hey {name}!");
        }
    }

    // ----- Shape-diff guardrail (E2E sanity) -----

    [Test]
    public async Task ShapeChangingEdit_IsRejectedAtTheStorageLayer()
    {
        await Store.UpsertAsync(
            "shape_e2e_v1",
            GreetDocuments.Casual,
            null,
            CancellationToken.None
        );

        // Aliasing greeting to message changes the response key the app reads.
        Func<Task> act = () =>
            Store.UpsertAsync(
                "shape_e2e_v1",
                GreetDocuments.RenamedField,
                null,
                CancellationToken.None
            );

        await act.Should().ThrowAsync<ShapeDiffViolationException>();
    }
}
