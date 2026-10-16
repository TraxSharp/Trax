using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Services;
using static Trax.Api.Tests.AuthE2E.AuthE2EHost;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// A client document reads a query model on a host that requires persisted operations: it runs by id, paged, filtered
/// and sorted as its text says, with only the filter's values as variables; a query model's own authorization still
/// checks the caller; and a document that passes a whole filter as a variable refuses the start.
/// <para>Enforces <c>Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md")]
[TestFixture]
[NonParallelizable]
public class ClientDocumentsQueryModelE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md";

    private const string Database = "trax_api_client_docs_models";

    private const string BookList =
        "query BookList($rating: Int, $after: String) { discover { library { bookRecords(first: 2, after: $after, "
        + "where: { rating: { eq: $rating } }, order: { title: ASC }) { totalCount nodes { title } } } } }";

    private const string AdminBooks =
        "query AdminBooks { discover { vault { ownedBooks { totalCount } } } }";

    private readonly List<string> _files = [];
    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        TestDbContext.EnsureSeeded(ConnectionString(Database));
        AuthzTestDbContext.EnsureSeeded(ConnectionString(Database));
        _host = await StartWithAsync(Database, ("BookList", BookList), ("AdminBooks", AdminBooks));
    }

    [OneTimeTearDown]
    public void Down()
    {
        _host.Dispose();
        foreach (var file in _files)
            File.Delete(file);
    }

    [Test]
    public async Task A_client_document_reads_a_query_model_by_id()
    {
        using var answer = await PostIdAsync(
            ClientDocuments.IdOf("BookList", BookList),
            new { rating = 5 },
            PlayerApiKey
        );

        var books = answer
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("library")
            .GetProperty("bookRecords");
        books.GetProperty("totalCount").GetInt32().Should().Be(3, answer.RootElement.ToString());
        books
            .GetProperty("nodes")
            .EnumerateArray()
            .Select(n => n.GetProperty("title").GetString())
            .Should()
            .Equal(
                ["Structure and Interpretation of Computer Programs", "The C Programming Language"],
                "the document's own filter, order and page run, per " + Adr
            );
    }

    [Test]
    public async Task An_authorized_query_model_in_a_client_document_still_checks_the_caller()
    {
        using var refused = await PostIdAsync(
            ClientDocuments.IdOf("AdminBooks", AdminBooks),
            new { },
            PlayerApiKey
        );
        using var allowed = await PostIdAsync(
            ClientDocuments.IdOf("AdminBooks", AdminBooks),
            new { },
            AdminApiKey
        );

        refused
            .RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeTrue("a document widens which shapes run, never who may read them, per " + Adr);
        errors.ToString().Should().Contain("TRAX_AUTHORIZATION");
        allowed
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("vault")
            .GetProperty("ownedBooks")
            .GetProperty("totalCount")
            .GetInt32()
            .Should()
            .BeGreaterThan(0, allowed.RootElement.ToString());
    }

    [Test]
    public async Task A_filter_passed_whole_as_a_variable_refuses_the_start()
    {
        const string database = "trax_api_client_docs_models_filter";
        TestDbContext.EnsureSeeded(ConnectionString(database));
        AuthzTestDbContext.EnsureSeeded(ConnectionString(database));

        var act = async () =>
        {
            using var host = await StartWithAsync(
                database,
                (
                    "AnyFilter",
                    "query AnyFilter($where: BookRecordFilterInput) { discover { library { "
                        + "bookRecords(where: $where) { totalCount } } } }"
                )
            );
        };

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*AnyFilter*'$where'*filter or sort input*", "per " + Adr);
    }

    private async Task<IHost> StartWithAsync(
        string database,
        params (string Name, string Text)[] documents
    )
    {
        var file = Path.Combine(Path.GetTempPath(), $"trax-documents-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            file,
            JsonSerializer.Serialize(
                new
                {
                    format = 1,
                    documents = documents.Select(d => new
                    {
                        name = d.Name,
                        source = $"src/{d.Name}.graphql",
                        text = d.Text,
                    }),
                }
            )
        );
        _files.Add(file);
        return await StartAsync(
            Schemes.ApiKey,
            database,
            graphql =>
                graphql.UsePersistedOperations(po =>
                    po.SingleNode().RequirePersisted(true).ClientDocuments(file)
                )
        );
    }

    private async Task<JsonDocument> PostIdAsync(string id, object variables, string apiKey)
    {
        var client = _host.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { id, variables }),
        };
        request.Headers.Add("X-Api-Key", apiKey);
        var response = await client.SendAsync(request);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
