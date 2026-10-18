using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Trax.Api.Dashboard.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.StateMachine.E2E.Fakes;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// A host that serves the operations console and requires persisted operations registers the console's documents
/// when it starts and tells the page their ids, so the console works where document text is refused: each document
/// runs by its id, its text is still refused, and the operations gate still decides who may run it.
/// <para>Enforces <c>Trax.Api/docs/adr/0042-the-operations-console-is-registered-as-persisted-documents.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0042-the-operations-console-is-registered-as-persisted-documents.md")]
[TestFixture]
[NonParallelizable]
public partial class ConsoleDocumentsE2ETests
{
    private const string Adr =
        "docs/adr/0042-the-operations-console-is-registered-as-persisted-documents.md";

    private const string Hosts =
        "query Hosts {\n  operations {\n    hosts {\n      instanceId\n      name\n      environment\n      lastSeen\n"
        + "      totalExecutions\n      currentlyRunning\n      __typename\n    }\n    __typename\n  }\n}";

    [Test]
    public async Task The_page_names_the_id_of_each_document_the_host_registered()
    {
        await using var host = await HostAsync("trax_sm_console_docs_ids", persisted: true);

        var ids = await DocumentIdsAsync(host.Host);

        ids.Keys.Should()
            .Contain(["Hosts", "Overview", "RequeueDeadLetter", "OnTrainStateChanged"]);
        ids.Values.Should().OnlyContain(id => id.StartsWith("trax_console_"));
        ids.Keys.Should()
            .NotContain(
                "PersistedOperations",
                "persisted-operation management runs as text, behind its own gate, per " + Adr
            );
    }

    [Test]
    public async Task A_console_document_runs_by_its_id_with_persisted_operations_required()
    {
        await using var host = await HostAsync("trax_sm_console_docs_run", persisted: true);
        var ids = await DocumentIdsAsync(host.Host);

        using var answer = await host.Host.PostPersistedAsync(
            ids["Hosts"],
            apiKey: E2EHost.OperatorKey
        );

        answer
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(MachineDocumentOps.Show(answer));
        answer
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("hosts")
            .ValueKind.Should()
            .Be(JsonValueKind.Array);
    }

    [Test]
    public async Task Its_text_sent_raw_is_still_refused()
    {
        await using var host = await HostAsync("trax_sm_console_docs_raw", persisted: true);

        using var answer = await host.Host.PostAsync(Hosts, apiKey: E2EHost.OperatorKey);

        (
            answer.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
        )
            .Should()
            .BeFalse("a console document runs by its id only, per " + Adr);
        answer.RootElement.GetProperty("errors").GetArrayLength().Should().BePositive();
    }

    [Test]
    public async Task The_operations_gate_still_decides_who_may_run_it()
    {
        await using var host = await HostAsync("trax_sm_console_docs_gate", persisted: true);
        var ids = await DocumentIdsAsync(host.Host);

        using var refused = await host.Host.PostPersistedAsync(
            ids["Hosts"],
            apiKey: E2EHost.BobKey
        );

        refused
            .RootElement.GetProperty("errors")
            .GetArrayLength()
            .Should()
            .BePositive(
                "registering a console document changes which shapes run, never who may run them, per "
                    + Adr
            );
    }

    [Test]
    public async Task Without_persisted_operations_the_page_names_no_ids()
    {
        await using var host = await HostAsync("trax_sm_console_docs_none", persisted: false);

        (await DocumentIdsAsync(host.Host)).Should().BeEmpty();
    }

    // The ids the page is told, read from the runtime config the dashboard writes into its entry page.
    private static async Task<Dictionary<string, string>> DocumentIdsAsync(IHost host)
    {
        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/trax/dashboard/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        var content = ConfigMeta().Match(html).Groups["content"].Value;
        using var config = JsonDocument.Parse(WebUtility.HtmlDecode(content));
        return config.RootElement.TryGetProperty("documents", out var documents)
            ? documents.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!)
            : [];
    }

    [GeneratedRegex("<meta name=\"trax-dashboard-config\" content=\"(?<content>[^\"]*)\"")]
    private static partial Regex ConfigMeta();

    private static E2EHostOptions Options(bool persisted) =>
        new()
        {
            // No machine served: a host serving one gets persisted operations whether the test asks or not.
            ExposeOperations = true,
            GraphQL = persisted
                ? graphql =>
                    graphql.UsePersistedOperations(po => po.SingleNode().RequirePersisted(true))
                : null,
            Endpoints = endpoints => endpoints.UseTraxApiDashboard(d => d.AllowAnonymousShell()),
        };

    private static async Task<HostedDatabase> HostAsync(string database, bool persisted)
    {
        await E2EHost.RecreateDatabaseAsync(database);
        try
        {
            var host = await E2EHost.StartAsync(database, new CountingCharge(), Options(persisted));
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
