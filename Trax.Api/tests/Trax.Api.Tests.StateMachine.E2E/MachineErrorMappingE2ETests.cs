using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// An infrastructure failure inside an event (the database refusing the advance) is answered as a typed problem
/// that says nothing about the server: no stack, no generic train failure text, no host, no SQL. Modelled on the
/// operations surface's masking (<c>OperationsFailureMaskingTests</c>).
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class MachineErrorMappingE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_machine_errors";

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(Database, new CountingCharge(), E2EHostOptions.Wizard());
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
    }

    [Test]
    public async Task An_infrastructure_failure_inside_advance_is_a_typed_problem_without_server_detail()
    {
        var paying = await _host.WizardInPayingAsync(E2EHost.AliceKey);

        // The draft table disappears under the host, as a broken connection or a failed migration would leave it.
        await E2ESql.ExecuteAsync(
            Database,
            "ALTER TABLE trax.snapshot_draft RENAME TO snapshot_draft_unavailable"
        );
        string body;
        try
        {
            using var doc = await _host.PostAsync(
                WizardOps.Event("back", paying.Id, WizardOps.NewRequestId()),
                apiKey: E2EHost.AliceKey
            );
            body = doc.RootElement.GetRawText();
            WizardOps
                .Result(doc, "back")
                .ProblemCode.Should()
                .NotBeNull("an infrastructure failure is answered as a typed problem, per " + Adr);
        }
        finally
        {
            await E2ESql.ExecuteAsync(
                Database,
                "ALTER TABLE trax.snapshot_draft_unavailable RENAME TO snapshot_draft"
            );
        }

        body.Should()
            .NotContain(" at ", "no stack trace reaches the client")
            .And.NotContain("The train failed.")
            .And.NotContain("snapshot_draft", "no table name")
            .And.NotContainEquivalentOf("relation")
            .And.NotContainEquivalentOf("localhost", "no host")
            .And.NotContain(TestPostgres.Port.ToString(), "no port")
            .And.NotContainEquivalentOf("select ", "no SQL")
            .And.NotContainEquivalentOf("update ", "no SQL");
    }
}
