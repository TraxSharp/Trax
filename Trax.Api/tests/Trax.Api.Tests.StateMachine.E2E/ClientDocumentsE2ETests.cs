using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.StateMachine;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// A host registers the documents its frontend wrote, named in <c>ClientDocuments(path)</c>, when it starts: each runs
/// by its id with persisted operations required, a document reaching anything but trains or past the host's own limits
/// refuses the start, and a version another node registered runs here while it is stored, so a frontend's deploy does
/// not strand the pages built before it.
/// <para>Enforces <c>Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md")]
[TestFixture]
[NonParallelizable]
public class ClientDocumentsE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md";

    // As trax client prints it: the operation, then each fragment it spreads.
    private const string OrderCard =
        "query OrderCard($input: FindOrderInput!) {\n  discover {\n    findOrder(input: $input) {\n      title\n"
        + "      customer {\n        ...CustomerName\n      }\n    }\n  }\n}\n\n"
        + "fragment CustomerName on CustomerOutput {\n  name\n}\n";

    private const string OrderCardV2 =
        "query OrderCard($input: FindOrderInput!) { discover { findOrder(input: $input) { title } } }";

    private const string NoteIt =
        "mutation NoteIt($input: RecordNoteInput!) { dispatch { recordNote(input: $input) { output { text } } } }";

    private readonly List<string> _files = [];

    [TearDown]
    public void CleanFiles()
    {
        foreach (var file in _files)
            File.Delete(file);
        _files.Clear();
    }

    [Test]
    public async Task A_client_document_registered_at_boot_runs_by_id()
    {
        await using var host = await HostAsync("trax_sm_client_docs_run", [OrderCard, NoteIt]);

        using var answer = await host.Host.PostPersistedAsync(
            ClientDocuments.IdOf("OrderCard", OrderCard),
            new { input = new { id = 7 } }
        );

        var order = answer
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("findOrder");
        order
            .GetProperty("title")
            .GetString()
            .Should()
            .Be("Order 7", MachineDocumentOps.Show(answer));
        order.GetProperty("customer").GetProperty("name").GetString().Should().Be("Ada");
        order.EnumerateObject().Select(p => p.Name).Should().Equal("title", "customer");
    }

    [Test]
    public async Task The_same_text_sent_raw_is_still_refused()
    {
        await using var host = await HostAsync("trax_sm_client_docs_raw", [OrderCard]);

        var client = host.Host.GetTestServer().CreateClient();
        var response = await client.PostAsJsonAsync(
            "/trax/graphql",
            new { query = OrderCard, variables = new { input = new { id = 7 } } }
        );
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        (
            body.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
        )
            .Should()
            .BeFalse(
                "a client document runs by its id only, with persisted operations required, per "
                    + Adr
            );
        body.RootElement.GetProperty("errors").GetArrayLength().Should().BePositive();
    }

    [Test]
    public async Task An_authorized_train_in_a_client_document_still_checks_the_caller()
    {
        await using var host = await HostAsync("trax_sm_client_docs_auth", [NoteIt]);
        var id = ClientDocuments.IdOf("NoteIt", NoteIt);

        using var refused = await host.Host.PostPersistedAsync(
            id,
            new { input = new { text = "x" } },
            E2EHost.BobKey
        );
        using var allowed = await host.Host.PostPersistedAsync(
            id,
            new { input = new { text = "hello" } },
            E2EHost.AliceKey
        );

        refused
            .RootElement.GetProperty("errors")
            .GetArrayLength()
            .Should()
            .BePositive("a client document widens which shapes run, never who may run them");
        allowed
            .RootElement.GetProperty("data")
            .GetProperty("dispatch")
            .GetProperty("recordNote")
            .GetProperty("output")
            .GetProperty("text")
            .GetString()
            .Should()
            .Be("hello", MachineDocumentOps.Show(allowed));
    }

    [TestCase("query Ops { operations { __typename } }", "*client document 'Ops'*")]
    [TestCase("query Wiz { machines { __typename } }", "*client document 'Wiz'*")]
    [TestCase(
        "mutation Two($input: RecordNoteInput!) { dispatch { a: recordNote(input: $input) { externalId } "
            + "b: recordNote(input: $input) { externalId } } }",
        "*client document 'Two'*runs 2 trains*"
    )]
    [TestCase(
        "query Gone($input: FindOrderInput!) { discover { findOrder(input: $input) { colour } } }",
        "*client document 'Gone'*refused by this host's own validation*"
    )]
    public async Task A_document_the_host_cannot_serve_refuses_the_start_naming_it(
        string text,
        string message
    )
    {
        var act = () => HostAsync("trax_sm_client_docs_refused", [text]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
    }

    [Test]
    public async Task A_document_past_the_hosts_depth_refuses_the_start()
    {
        var act = () => HostAsync("trax_sm_client_docs_depth", [OrderCard], maxExecutionDepth: 3);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*client document 'OrderCard'*refused by this host's own validation*");
    }

    [Test]
    public async Task A_missing_file_refuses_the_start_naming_it()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"trax-missing-{Guid.NewGuid():N}.json");

        var act = () => HostAsync("trax_sm_client_docs_missing", [], file: missing);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*'{missing}'*cannot be read*");
    }

    [Test]
    public async Task A_version_another_node_registered_runs_on_this_one()
    {
        const string database = "trax_sm_client_docs_versions";
        var v1 = DocumentsFile(("OrderCard", OrderCard));
        var v2 = DocumentsFile(("OrderCard", OrderCardV2));
        await E2EHost.RecreateDatabaseAsync(database);
        var a = await E2EHost.StartAsync(database, new CountingCharge(), Options(v1, "a"));
        IHost? b = null;
        try
        {
            b = await E2EHost.StartAsync(database, new CountingCharge(), Options(v2, "b"));
            using (
                var old = await b.PostPersistedAsync(
                    ClientDocuments.IdOf("OrderCard", OrderCard),
                    new { input = new { id = 1 } }
                )
            )
                old.RootElement.GetProperty("data")
                    .GetProperty("discover")
                    .GetProperty("findOrder")
                    .GetProperty("customer")
                    .GetProperty("name")
                    .GetString()
                    .Should()
                    .Be(
                        "Ada",
                        "a page built before the frontend's deploy keeps working on a node with the new "
                            + "documents, per "
                            + Adr
                    );

            // Node A started before B registered v2: it answers SERVER_OUTDATED until its next read of the store,
            // then runs it.
            var newer = ClientDocuments.IdOf("OrderCard", OrderCardV2);
            string? title = null;
            var ran = await E2EWait.UntilAsync(async () =>
            {
                using var answer = await a.PostPersistedAsync(
                    newer,
                    new { input = new { id = 2 } }
                );
                if (
                    answer.RootElement.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Object
                )
                {
                    title = data.GetProperty("discover")
                        .GetProperty("findOrder")
                        .GetProperty("title")
                        .GetString();
                    return true;
                }
                MachineDocumentOps
                    .Codes(answer)
                    .Should()
                    .Equal([RefusalCode.ServerOutdated.ToWire()], MachineDocumentOps.Show(answer));
                return false;
            });
            ran.Should().BeTrue("node A runs v2 once it has read the store again");
            title.Should().Be("Order 2");
        }
        finally
        {
            b?.Dispose();
            a.Dispose();
            await E2EHost.DropDatabaseAsync(database);
        }
    }

    [Test]
    public async Task A_client_id_no_node_registered_is_outdated()
    {
        await using var host = await HostAsync("trax_sm_client_docs_unknown", [OrderCard]);
        var made = ClientDocuments.IdOf("OrderCard", OrderCard + " ");

        string[] codes = [];
        await E2EWait.UntilAsync(async () =>
        {
            using var answer = await host.Host.PostPersistedAsync(
                made,
                new { input = new { id = 1 } }
            );
            codes = [.. MachineDocumentOps.Codes(answer)];
            return codes.SequenceEqual([RefusalCode.ClientOutdated.ToWire()]);
        });
        codes.Should().Equal([RefusalCode.ClientOutdated.ToWire()]);
    }

    private string DocumentsFile(params (string Name, string Text)[] documents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"trax-documents-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            path,
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
        _files.Add(path);
        return path;
    }

    private static E2EHostOptions Options(
        string file,
        string node,
        int? maxExecutionDepth = null
    ) =>
        new()
        {
            Served = [typeof(WizardMachine)],
            ApplicationName = $"client_docs_{node}",
            GraphQL = graphql =>
            {
                graphql = graphql.UsePersistedOperations(po =>
                    po.SingleNode().RequirePersisted(true).ClientDocuments(file)
                );
                return maxExecutionDepth is { } depth ? graphql.MaxExecutionDepth(depth) : graphql;
            },
        };

    private async Task<HostedDatabase> HostAsync(
        string database,
        string[] texts,
        string? file = null,
        int? maxExecutionDepth = null
    )
    {
        file ??= DocumentsFile(texts.Select(t => (t.Split(' ', '(', '{')[1], t)).ToArray());
        await E2EHost.RecreateDatabaseAsync(database);
        try
        {
            var host = await E2EHost.StartAsync(
                database,
                new CountingCharge(),
                Options(file, "single", maxExecutionDepth)
            );
            return new HostedDatabase(database, host);
        }
        catch
        {
            await E2EHost.DropDatabaseAsync(database);
            throw;
        }
    }

    private sealed record HostedDatabase(string Database, IHost Host) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Host.Dispose();
            await E2EHost.DropDatabaseAsync(Database);
        }
    }
}
