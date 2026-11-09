using System.Text.Json;
using AwesomeAssertions;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// A host that requires persisted operations serves its views from documents it generates and registers itself, as it
/// does a machine's (ADR <c>Trax.Docs/adr/0058</c>): the view's text sent raw is refused, its document runs, and the
/// view's read rule still refuses a caller who is not one of its readers.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MachineViewDocumentsTests
{
    private const string Database = "trax_sm_machine_view_documents";

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        var (machines, host) = IrMachineViewsE2ETests.Machines();
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Instances = machines,
                Views = new MachineViewCatalog(host.Views),
                ApplicationName = "view_docs",
                GraphQL = graphql =>
                    graphql.UsePersistedOperations(po => po.SingleNode().RequirePersisted(true)),
                Services = s =>
                {
                    s.AddSingleton(host);
                    s.AddHostedService<SystemInstanceStarter>();
                },
            }
        );
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
    }

    [Test]
    public async Task A_views_text_sent_raw_is_refused()
    {
        using var answer = await _host.PostAsync(
            "{ views { shopSign { vShop { open } } } }",
            apiKey: E2EHost.BobKey
        );

        answer.RootElement.TryGetProperty("errors", out _).Should().BeTrue();
        answer.RootElement.TryGetProperty("data", out var data).Should().BeFalse(data.ToString());
    }

    [Test]
    public async Task A_views_generated_document_runs_by_its_id()
    {
        using var answer = await _host.PostPersistedAsync(
            await IdAsync("shopSign"),
            apiKey: E2EHost.BobKey
        );

        answer
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(MachineDocumentOps.Show(answer));
        answer
            .RootElement.GetProperty("data")
            .GetProperty("views")
            .GetProperty("shopSign")
            .GetProperty("vShop")
            .GetProperty("open")
            .GetString()
            .Should()
            .Be("Closed for now");
    }

    [Test]
    public async Task Its_document_still_refuses_a_caller_who_is_not_a_reader()
    {
        using var answer = await _host.PostPersistedAsync(
            await IdAsync("orders"),
            apiKey: E2EHost.BobKey
        );

        answer
            .RootElement.GetProperty("errors")[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("FORBIDDEN");
    }

    private async Task<string> IdAsync(string field)
    {
        var executor = await _host
            .Services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
        return ViewDocuments.Generate(executor.Schema).Single(d => d.Field == field).Id;
    }
}
