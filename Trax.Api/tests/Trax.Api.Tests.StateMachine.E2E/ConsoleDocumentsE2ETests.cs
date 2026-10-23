using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Routing;
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

        using var answer = await host.Host.PostAsync(
            ConsoleTextOf("Hosts"),
            apiKey: E2EHost.OperatorKey
        );

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

    [Test]
    public async Task A_host_that_registers_the_documents_without_the_page_runs_them_by_id()
    {
        await using var host = await HostAsync(
            "trax_sm_console_docs_bare",
            PageLess(persisted: true)
        );

        using var answer = await host.Host.PostPersistedAsync(
            ConsoleIdOf("Hosts"),
            apiKey: E2EHost.OperatorKey
        );
        var page = await host.Host.GetTestServer().CreateClient().GetAsync("/trax/dashboard/");

        answer
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(MachineDocumentOps.Show(answer));
        page.StatusCode.Should().Be(HttpStatusCode.NotFound, "no page is served");
    }

    [Test]
    public async Task Without_the_page_its_text_sent_raw_is_still_refused()
    {
        await using var host = await HostAsync(
            "trax_sm_console_docs_bare_raw",
            PageLess(persisted: true)
        );

        using var answer = await host.Host.PostAsync(
            ConsoleTextOf("Hosts"),
            apiKey: E2EHost.OperatorKey
        );

        (
            answer.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
        )
            .Should()
            .BeFalse("a console document runs by its id only, per " + Adr);
        answer.RootElement.GetProperty("errors").GetArrayLength().Should().BePositive();
    }

    [Test]
    public async Task Without_the_page_the_operations_gate_still_decides_who_may_run_it()
    {
        await using var host = await HostAsync(
            "trax_sm_console_docs_bare_gate",
            PageLess(persisted: true)
        );

        using var refused = await host.Host.PostPersistedAsync(
            ConsoleIdOf("Hosts"),
            apiKey: E2EHost.BobKey
        );

        MachineDocumentOps
            .Codes(refused)
            .Should()
            .IntersectWith(
                AuthorizationCodes,
                "registering a console document changes which shapes run, never who may run them, per "
                    + Adr
            );
    }

    [Test]
    public async Task Without_the_page_a_stream_is_still_refused_to_a_caller_who_may_see_nothing()
    {
        await using var host = await HostAsync(
            "trax_sm_console_docs_bare_stream",
            PageLess(persisted: true)
        );
        await using var ws = await GraphQLWs.ConnectAsync(host.Host, E2EHost.BobKey);

        await ws.SubscribePersistedAsync("data", ConsoleIdOf("OnDataChanged"));
        var (refused, codes, raw) = await ws.RefusalAsync("data");

        refused.Should().BeTrue(raw);
        codes
            .Should()
            .IntersectWith(
                AuthorizationCodes,
                "a stream document is authorized per subscriber, per " + Adr
            );
    }

    [Test]
    public async Task Without_persisted_operations_registering_the_documents_without_the_page_does_nothing()
    {
        await using var host = await HostAsync(
            "trax_sm_console_docs_bare_none",
            PageLess(persisted: false)
        );

        using var byId = await host.Host.PostPersistedAsync(
            ConsoleIdOf("Hosts"),
            apiKey: E2EHost.OperatorKey
        );
        using var asText = await host.Host.PostAsync(
            ConsoleTextOf("Hosts"),
            apiKey: E2EHost.OperatorKey
        );

        byId.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeTrue("no document was registered: " + MachineDocumentOps.Show(byId));
        asText
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(MachineDocumentOps.Show(asText));
    }

    [Test]
    public async Task The_page_and_the_page_less_call_together_register_each_document_once()
    {
        const string database = "trax_sm_console_docs_both";
        var options = Options(persisted: true) with
        {
            Endpoints = endpoints =>
            {
                endpoints.UseTraxApiDashboard(d => d.AllowAnonymousShell());
                endpoints.UseTraxOperatorConsoleDocuments();
            },
        };
        await using var host = await HostAsync(database, options);

        var ids = await DocumentIdsAsync(host.Host);
        var stored = await E2ESql.ScalarAsync<long>(
            database,
            "SELECT count(*) FROM trax.persisted_operation WHERE tenant_key = '' AND starts_with(id, 'trax_console_')"
        );

        ids.Should().NotBeEmpty();
        ids.Should().OnlyContain(pair => pair.Value == ConsoleIdOf(pair.Key));
        stored.Should().Be(ids.Count, "each document is registered once, under one id");
    }

    [Test]
    public async Task Registering_the_documents_after_the_host_started_throws()
    {
        IEndpointRouteBuilder? mapped = null;
        var options = Options(persisted: true) with
        {
            Endpoints = endpoints =>
            {
                endpoints.UseTraxOperatorConsoleDocuments();
                mapped = endpoints;
            },
        };
        await using var host = await HostAsync("trax_sm_console_docs_late", options);

        var console = () => mapped!.UseTraxOperatorConsoleDocuments();
        var dashboard = () => mapped!.UseTraxApiDashboard(d => d.AllowAnonymousShell());

        console.Should().Throw<InvalidOperationException>().WithMessage("*after the host started*");
        dashboard
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*after the host started*");
    }

    // The id a host gives a console document, from the documents the dashboard package embeds.
    private static string ConsoleIdOf(string name) =>
        Trax.Api.GraphQL.PersistedOperations.Services.ConsoleDocuments.IdOf(
            name,
            ConsoleTextOf(name)
        );

    // A console document's text as the dashboard package embeds it, so a test that sends it raw sends a valid document.
    private static string ConsoleTextOf(string name)
    {
        using var stream =
            typeof(ApiDashboardEndpointExtensions).Assembly.GetManifestResourceStream(
                "TraxApiDashboardDocuments/console-documents.json"
            )!;
        using var file = JsonDocument.Parse(stream);
        return file
            .RootElement.GetProperty("documents")
            .EnumerateArray()
            .Single(d => d.GetProperty("name").GetString() == name)
            .GetProperty("text")
            .GetString()!;
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

    // A host that registers the documents without serving the page.
    private static E2EHostOptions PageLess(bool persisted) =>
        Options(persisted) with
        {
            Endpoints = endpoints => endpoints.UseTraxOperatorConsoleDocuments(),
        };

    // The codes a request refused for who is asking carries.
    private static readonly string[] AuthorizationCodes =
    [
        "AUTH_NOT_AUTHENTICATED",
        "AUTH_NOT_AUTHORIZED",
        "TRAX_AUTHORIZATION",
    ];

    private static Task<HostedDatabase> HostAsync(string database, bool persisted) =>
        HostAsync(database, Options(persisted));

    private static async Task<HostedDatabase> HostAsync(string database, E2EHostOptions options)
    {
        await E2EHost.RecreateDatabaseAsync(database);
        try
        {
            var host = await E2EHost.StartAsync(database, new CountingCharge(), options);
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
