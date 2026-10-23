using System.Reflection;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Console.Terminal.Connection;
using Trax.Api.Console.Terminal.Headless;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Operations;
using Trax.Api.Dashboard.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Client;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.StateMachine.E2E.TerminalConsole;

/// <summary>
/// trax-console against a host that requires persisted operations: it sends the browser console's documents by the
/// ids the host registers, reads a list and acts on a row through them, a document the host did not register is
/// refused, and the operations gate decides as it does for the browser.
/// <para>Enforces <c>Trax.Docs/adr/0057-the-operations-console-sends-only-documents-trax-client-generated.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0057-the-operations-console-sends-only-documents-trax-client-generated.md"
)]
[TestFixture]
[NonParallelizable]
public class TraxConsoleE2ETests
{
    [Test]
    public async Task It_reads_the_hosts_release_and_finds_it_is_its_own()
    {
        await using var host = await HostAsync("trax_sm_tui_version");
        await using var session = host.Session(E2EHost.OperatorKey);

        var version = await session.CheckVersionAsync();

        version.Should().BeOfType<VersionCheck.Same>();
    }

    [Test]
    public async Task It_lists_runs_by_the_documents_id_with_no_text()
    {
        await using var host = await HostAsync("trax_sm_tui_runs");
        await using var session = host.Session(E2EHost.OperatorKey);

        var page = await Catalog.Runs.LoadAsync(
            session.Client,
            ListQuery.First(),
            CancellationToken.None
        );

        page.Succeeded.Should().BeTrue(page.Message);
        host.Sent.Should()
            .ContainSingle(b => b["operationName"]!.GetValue<string>() == "Executions")
            .Which.Should()
            .Match<JsonObject>(b =>
                b["id"]!.GetValue<string>().StartsWith("trax_console_Executions_")
                && !b.ContainsKey("query")
            );
    }

    [Test]
    public async Task It_requeues_a_dead_letter_through_the_hosts_scheduler()
    {
        await using var host = await HostAsync("trax_sm_tui_requeue");
        await using var session = host.Session(E2EHost.OperatorKey);

        var outcome = await Catalog.DeadLetters.RunAsync(
            session.Client,
            "requeue",
            [7, 9],
            new ActionOptions(),
            CancellationToken.None
        );

        outcome.Succeeded.Should().BeTrue(outcome.Message);
        host.Scheduler.Requeued.Should().Equal(7, 9);
    }

    [Test]
    public async Task A_document_the_host_did_not_register_is_refused()
    {
        await using var host = await HostAsync("trax_sm_tui_unregistered");
        await using var session = host.Session(E2EHost.OperatorKey);
        var registered = TraxOperations.Executions;

        var byId = await session.Client.SendAsync(
            registered with
            {
                Id = "trax_console_Executions_" + new string('0', 64) + new string('0', 16),
            },
            new ExecutionsVariables { Take = 1 }
        );
        var byText = await session.Client.Transport.ExecuteAsync(
            new TraxRequest(
                new TraxDocument<TraxNoVariables, JsonObject>(
                    "Executions",
                    TraxOperationKind.Query,
                    null,
                    "query Executions { operations { executions(take: 1) { totalCount } } }"
                ),
                new JsonObject()
            )
        );

        byId.Succeeded.Should().BeFalse();
        byId.Code.Should()
            .BeOneOf(
                ["CLIENT_OUTDATED", "SERVER_OUTDATED"],
                "an id this host never registered is one of another release, whichever is newer"
            );
        byText
            .Errors.Should()
            .ContainSingle()
            .Which.Code.Should()
            .Be("PERSISTED_OPERATION_REQUIRED");
    }

    [Test]
    public async Task The_operations_gate_still_decides()
    {
        await using var host = await HostAsync("trax_sm_tui_gate");
        await using var session = host.Session(E2EHost.BobKey);

        var page = await Catalog.Runs.LoadAsync(
            session.Client,
            ListQuery.First(),
            CancellationToken.None
        );

        page.Succeeded.Should().BeFalse("a caller without the operator role reaches no operations");
        host.Scheduler.Requeued.Should().BeEmpty();
    }

    [Test]
    public async Task A_host_that_registers_the_documents_without_the_page_serves_it()
    {
        await using var host = await HostAsync("trax_sm_tui_bare", page: false);
        await using var session = host.Session(E2EHost.OperatorKey);

        var version = await session.CheckVersionAsync();
        var page = await Catalog.Manifests.LoadAsync(
            session.Client,
            ListQuery.First(),
            CancellationToken.None
        );

        version.Should().BeOfType<VersionCheck.Same>();
        page.Succeeded.Should().BeTrue(page.Message);
    }

    [Test]
    public async Task The_headless_list_prints_one_row_per_line_and_the_page_on_standard_error()
    {
        await using var host = await HostAsync("trax_sm_tui_headless");
        await using var session = host.Session(E2EHost.OperatorKey);
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await HeadlessCommands.ListAsync(
            session,
            "dead-letters",
            new Dictionary<string, string> { ["status"] = "awaiting_intervention" },
            take: 10,
            after: null,
            output,
            error,
            CancellationToken.None
        );

        exit.Should().Be(0, error.ToString());
        output.ToString().Should().BeEmpty("the host has no dead letters");
        JsonNode.Parse(error.ToString())!["totalCount"]!.GetValue<int>().Should().Be(0);
        host.Sent.Single()["variables"]!["status"]!
            .GetValue<string>()
            .Should()
            .Be("AWAITING_INTERVENTION");
    }

    private static async Task<ConsoleHost> HostAsync(string database, bool page = true)
    {
        await E2EHost.RecreateDatabaseAsync(database);
        var scheduler = DispatchProxy.Create<ITraxScheduler, RecordingScheduler>();
        try
        {
            var host = await E2EHost.StartAsync(
                database,
                new CountingCharge(),
                new E2EHostOptions
                {
                    ExposeOperations = true,
                    GraphQL = graphql =>
                        graphql.UsePersistedOperations(po =>
                            po.SingleNode().RequirePersisted(true)
                        ),
                    Endpoints = endpoints =>
                    {
                        if (page)
                            endpoints.UseTraxApiDashboard(d => d.AllowAnonymousShell());
                        else
                            endpoints.UseTraxOperatorConsoleDocuments();
                    },
                    Services = services => services.AddScoped(_ => scheduler),
                }
            );
            return new ConsoleHost(database, host, (RecordingScheduler)(object)scheduler);
        }
        catch
        {
            await E2EHost.DropDatabaseAsync(database);
            throw;
        }
    }

    private sealed class ConsoleHost(string database, IHost host, RecordingScheduler scheduler)
        : IAsyncDisposable
    {
        private readonly Recording _recording = new(host.GetTestServer().CreateHandler());

        public RecordingScheduler Scheduler => scheduler;

        /// <summary>Every body the console sent, in order, but the version check.</summary>
        public IReadOnlyList<JsonObject> Sent =>
            _recording
                .Bodies.Where(b => b["operationName"]?.GetValue<string>() != "ServerVersion")
                .ToList();

        public ConsoleSession Session(string apiKey) =>
            ConsoleSession.Open(
                ConsoleTarget.Resolve(
                    "http://localhost",
                    ConsoleTarget.DefaultGraphQLPath,
                    apiKeyFile: null,
                    tokenFile: null,
                    sendText: false,
                    name => name == ConsoleTarget.ApiKeyVariable ? apiKey : null
                ),
                _recording
            );

        public async ValueTask DisposeAsync()
        {
            host.Dispose();
            _recording.Dispose();
            await E2EHost.DropDatabaseAsync(database);
        }
    }

    // Keeps every body sent to the test server.
    private sealed class Recording(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly List<JsonObject> _bodies = [];

        public IReadOnlyList<JsonObject> Bodies
        {
            get
            {
                lock (_bodies)
                    return _bodies.ToList();
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            lock (_bodies)
                _bodies.Add((JsonObject)body!);
            return await base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>A scheduler that records the dead letters it is asked to requeue; any other call throws.</summary>
    public class RecordingScheduler : DispatchProxy
    {
        private readonly List<long> _requeued = [];

        public IReadOnlyList<long> Requeued
        {
            get
            {
                lock (_requeued)
                    return _requeued.ToList();
            }
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ITraxScheduler.RequeueDeadLettersAsync))
            {
                var ids = (long[])args![0]!;
                lock (_requeued)
                    _requeued.AddRange(ids);
                return Task.FromResult(
                    new BatchDeadLetterResult(ids.Length, $"Requeued {ids.Length}.")
                );
            }
            throw new InvalidOperationException(
                $"This host's scheduler does not {targetMethod?.Name}."
            );
        }
    }
}
