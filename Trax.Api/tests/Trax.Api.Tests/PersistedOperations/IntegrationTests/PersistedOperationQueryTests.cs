using System.Text.Json;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.Tests.PersistedOperations.Fixtures;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

/// <summary>
/// Drives the management queries end-to-end through HotChocolate. Each test
/// seeds rows via <see cref="IPersistedOperationStore"/> and then exercises
/// the GraphQL surface, asserting on filter, ordering, pagination, and
/// per-column projection. Failure of any of these signals a specific
/// regression named in the test method.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PersistedOperationQueryTests
{
    private ServiceProvider _sp = null!;
    private IRequestExecutor _executor = null!;
    private IPersistedOperationStore _store = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        if (!PostgresFixture.IsPostgresReachable())
            Assert.Ignore("Postgres not reachable; skipping integration tests.");

        _sp = await GraphQLFixture.BuildAsync();
        _executor = await GraphQLFixture.GetExecutorAsync(_sp);
        _store = _sp.GetRequiredService<IPersistedOperationStore>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_sp is not null)
            await _sp.DisposeAsync();
    }

    [SetUp]
    public async Task SetUp() => await PostgresFixture.ClearAsync();

    [Test]
    public async Task List_NoFilter_ReturnsAllRows_NewestUpdatedFirst()
    {
        // Seed three rows in order; ordering of the returned page must
        // reflect UpdatedAt descending, not insertion order.
        await _store.UpsertAsync("a_v1", GraphQLFixture.ValidDocument, null, default);
        await _store.UpsertAsync("b_v1", GraphQLFixture.ValidDocument, null, default);
        await _store.UpsertAsync("c_v1", GraphQLFixture.ValidDocument, null, default);
        // Touch a_v1 so its UpdatedAt moves to the top.
        await _store.UpsertAsync(
            "a_v1",
            GraphQLFixture.ValidDocument,
            new UpsertOptions { Description = "touched" },
            default
        );

        var page = await ListAsync(filter: null);

        page.GetProperty("totalCount").GetInt32().Should().Be(3);
        var ids = page.GetProperty("items")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetString())
            .ToList();
        ids[0].Should().Be("a_v1", "a_v1 was most recently upserted");
        ids.Should().Contain(new[] { "b_v1", "c_v1" });
    }

    [Test]
    public async Task List_FilterByActiveFalse_ReturnsOnlyDeactivated()
    {
        await _store.UpsertAsync("active_v1", GraphQLFixture.ValidDocument, null, default);
        await _store.UpsertAsync("inactive_v1", GraphQLFixture.ValidDocument, null, default);
        await _store.DeactivateAsync("inactive_v1", null, "test", default);

        var page = await ListAsync(
            filter: new Dictionary<string, object?> { ["isActive"] = false }
        );

        page.GetProperty("totalCount").GetInt32().Should().Be(1);
        page.GetProperty("items")[0].GetProperty("id").GetString().Should().Be("inactive_v1");
        page.GetProperty("items")[0].GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Test]
    public async Task List_FilterByIdPrefix_ReturnsOnlyMatching()
    {
        await _store.UpsertAsync("greet_v1", GraphQLFixture.ValidDocument, null, default);
        await _store.UpsertAsync("greet_v2", GraphQLFixture.ValidDocument, null, default);
        await _store.UpsertAsync("lookup_v1", GraphQLFixture.ValidDocument, null, default);

        var page = await ListAsync(
            filter: new Dictionary<string, object?> { ["idStartsWith"] = "greet" }
        );

        page.GetProperty("totalCount").GetInt32().Should().Be(2);
        page.GetProperty("items")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetString())
            .Should()
            .BeEquivalentTo("greet_v1", "greet_v2");
    }

    [Test]
    public async Task List_Pagination_TakeAndSkipReturnTheCorrectWindow()
    {
        for (var i = 0; i < 5; i++)
            await _store.UpsertAsync($"op_{i}_v1", GraphQLFixture.ValidDocument, null, default);

        var page = await ListAsync(filter: null, take: 2, skip: 1);

        // totalCount counts the full result, page items reflect the window.
        page.GetProperty("totalCount").GetInt32().Should().Be(5);
        page.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Test]
    public async Task Single_ExistingId_ReturnsRowWithEveryColumn()
    {
        await _store.UpsertAsync(
            "single_v1",
            GraphQLFixture.ValidDocument,
            new UpsertOptions { Description = "first upload" },
            default
        );

        var row = await SingleAsync("single_v1");

        row.ValueKind.Should().NotBe(JsonValueKind.Null);
        row.GetProperty("id").GetString().Should().Be("single_v1");
        // OperationName is taken from the document, not the id, post-refactor.
        row.GetProperty("operationName").GetString().Should().Be("Greet");
        row.GetProperty("description").GetString().Should().Be("first upload");
        row.GetProperty("isActive").GetBoolean().Should().BeTrue();
        row.GetProperty("shapeFingerprint").GetString().Should().HaveLength(64);
        row.GetProperty("document").GetString().Should().Be(GraphQLFixture.ValidDocument);
    }

    [Test]
    public async Task Single_MissingId_ReturnsNull()
    {
        var row = await SingleAsync("does_not_exist_v1");

        row.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public async Task History_ReturnsOneRowPerUpsertAndDeactivate_NewestFirst()
    {
        await _store.UpsertAsync("history_v1", GraphQLFixture.ValidDocument, null, default);
        await _store.UpsertAsync(
            "history_v1",
            GraphQLFixture.ValidDocument,
            new UpsertOptions { Description = "second upsert" },
            default
        );
        await _store.DeactivateAsync("history_v1", null, "rotating", default);

        var entries = await HistoryAsync("history_v1");

        // Two upserts + one deactivate = three history rows.
        entries.GetArrayLength().Should().Be(3);
        // Most recent first: deactivate, then second upsert, then first.
        entries[0].GetProperty("changeType").GetString().Should().Be("Deactivate");
        entries[0].GetProperty("changedReason").GetString().Should().Be("rotating");
    }

    [Test]
    public async Task History_UnknownId_ReturnsEmptyArray()
    {
        var entries = await HistoryAsync("never_existed_v1");

        entries.GetArrayLength().Should().Be(0);
    }

    private async Task<JsonElement> ListAsync(
        IReadOnlyDictionary<string, object?>? filter,
        int? take = null,
        int? skip = null
    )
    {
        var variables = new Dictionary<string, object?>();
        if (filter is not null)
            variables["filter"] = filter;
        if (take is not null)
            variables["take"] = take;
        if (skip is not null)
            variables["skip"] = skip;

        var json = await Execute(
            """
            query List($filter: PersistedOperationFilterInput, $take: Int! = 50, $skip: Int! = 0) {
              operations {
                persistedOperations {
                  persistedOperations(filter: $filter, take: $take, skip: $skip) {
                    totalCount
                    items { id operationName isActive shapeFingerprint description document }
                  }
                }
              }
            }
            """,
            variables
        );

        return json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("persistedOperations");
    }

    private async Task<JsonElement> SingleAsync(string id)
    {
        var json = await Execute(
            """
            query Single($id: String!) {
              operations {
                persistedOperations {
                  persistedOperation(id: $id) {
                    id
                    operationName
                    isActive
                    document
                    shapeFingerprint
                    description
                  }
                }
              }
            }
            """,
            new Dictionary<string, object?> { ["id"] = id }
        );

        return json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("persistedOperation");
    }

    private async Task<JsonElement> HistoryAsync(string id)
    {
        var json = await Execute(
            """
            query History($id: String!) {
              operations {
                persistedOperations {
                  persistedOperationHistory(id: $id) {
                    historyId
                    changeType
                    changedReason
                    shapeFingerprint
                  }
                }
              }
            }
            """,
            new Dictionary<string, object?> { ["id"] = id }
        );

        return json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("persistedOperationHistory");
    }

    private async Task<JsonDocument> Execute(
        string query,
        IReadOnlyDictionary<string, object?> variables
    )
    {
        var request = OperationRequestBuilder
            .New()
            .SetDocument(query)
            .SetVariableValues(variables.ToDictionary(p => p.Key, p => p.Value))
            .Build();
        var result = await _executor.ExecuteAsync(request);
        var op = result as IOperationResult;
        op.Should().NotBeNull();
        op!.Errors.Should().BeNullOrEmpty();
        return JsonDocument.Parse(op.ToJson());
    }
}
