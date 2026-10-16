using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.StateMachine;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Every train a host exposes gets a persisted document it registers itself, under <c>trax_train_</c>, which a client
/// runs by id; an id of the same train in another shape is answered as outdated, never with a validation error or a
/// server error; and the client manifest lists each train with what its attributes require of a caller.
/// <para>Enforces <c>docs/adr/0040-every-exposed-train-is-a-persisted-document-the-host-registers.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0040-every-exposed-train-is-a-persisted-document-the-host-registers.md")]
[TestFixture]
[NonParallelizable]
public class TrainDocumentsE2ETests
{
    private const string Adr =
        "docs/adr/0040-every-exposed-train-is-a-persisted-document-the-host-registers.md";

    private const string Database = "trax_sm_train_documents";

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Served = [typeof(WizardMachine)],
                GraphQL = MachineDocumentOps.Persisted(),
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
    public async Task Every_exposed_train_has_a_registered_document()
    {
        var ids = await TrainIdsAsync();

        ids.Should()
            .Contain(id => id.StartsWith("trax_train_query__ping_", StringComparison.Ordinal));
        ids.Should()
            .Contain(id =>
                id.StartsWith("trax_train_mutation__recordNote_", StringComparison.Ordinal)
            );
        ids.Should()
            .OnlyContain(
                id => TrainDocuments.SlotOf(id) != null,
                "every exposed train is registered under its own slot, per " + Adr
            );
    }

    [Test]
    public async Task A_train_runs_by_its_id()
    {
        var id = (await TrainIdsAsync()).Single(i =>
            i.StartsWith("trax_train_mutation__recordNote_", StringComparison.Ordinal)
        );

        using var answer = await _host.PostPersistedAsync(
            id,
            new { input = new { text = "hello" } },
            E2EHost.AliceKey
        );

        answer
            .RootElement.GetProperty("data")
            .GetProperty("dispatch")
            .GetProperty("recordNote")
            .GetProperty("output")
            .GetProperty("text")
            .GetString()
            .Should()
            .Be("hello", MachineDocumentOps.Show(answer));
    }

    [Test]
    public async Task An_id_of_another_shape_is_answered_as_outdated_never_as_a_server_error()
    {
        var made =
            TrainDocuments.SlotOf("mutation", null, "recordNote")
            + "_"
            + new string('9', MachineDocuments.IrHashLength + MachineDocuments.TextHashLength);

        var (status, body) = await _host.PostPersistedWithStatusAsync(
            made,
            new { input = new { text = "x" } },
            E2EHost.AliceKey
        );

        using (body)
        {
            MachineDocumentOps
                .Codes(body)
                .Should()
                .ContainSingle()
                .Which.Should()
                .BeOneOf(RefusalCode.ServerOutdated.ToWire(), RefusalCode.ClientOutdated.ToWire());
            status
                .Should()
                .BeInRange(400, 499, "an outdated id is the client's to fix, per " + Adr);
        }
    }

    [Test]
    public async Task A_host_exposing_a_back_reference_starts_and_serves_the_train()
    {
        var id = (await TrainIdsAsync()).Single(i =>
            i.StartsWith("trax_train_query__findOrder_", StringComparison.Ordinal)
        );

        using var answer = await _host.PostPersistedAsync(id, new { input = new { id = 7 } });

        var order = answer
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("findOrder");
        order
            .GetProperty("title")
            .GetString()
            .Should()
            .Be("Order 7", MachineDocumentOps.Show(answer));
        var customer = order.GetProperty("customer");
        customer.GetProperty("name").GetString().Should().Be("Ada");
        customer
            .TryGetProperty("orders", out _)
            .Should()
            .BeFalse("the back-reference is left out of the generated document, per " + Adr);
    }

    [Test]
    public async Task A_generated_document_past_the_hosts_depth_refuses_the_start_naming_the_train()
    {
        const string database = "trax_sm_train_documents_depth";
        await E2EHost.RecreateDatabaseAsync(database);
        try
        {
            var act = async () =>
            {
                using var host = await E2EHost.StartAsync(
                    database,
                    new CountingCharge(),
                    new E2EHostOptions
                    {
                        Served = [typeof(WizardMachine)],
                        GraphQL = graphql =>
                            MachineDocumentOps.Persisted()(graphql).MaxExecutionDepth(3),
                    }
                );
            };

            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*IFindOrder*refused by this host's own validation*");
        }
        finally
        {
            await E2EHost.DropDatabaseAsync(database);
        }
    }

    [Test]
    public async Task The_manifest_lists_each_train_with_what_it_requires()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trax-trains-{Guid.NewGuid():N}.json");
        try
        {
            await ClientManifestExport.WriteAsync(_host.Services, path, CancellationToken.None);
            var manifest = ClientManifest.Read(await File.ReadAllTextAsync(path));

            var record = manifest.Trains.Single(t => t.Field == "recordNote");
            record.Root.Should().Be("mutation");
            record.Requires!.RoleSets.Should().ContainSingle().Which.Should().Equal("Approver");
            record.Documents.Single().Id.Should().StartWith("trax_train_mutation__recordNote_");
            manifest.Trains.Single(t => t.Field == "ping").Requires!.Anonymous.Should().BeTrue();
            manifest.Types.Should().Contain("input RecordNoteInput").And.NotContain("operations");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<IReadOnlyList<string>> TrainIdsAsync()
    {
        await using var connection = new NpgsqlConnection(E2EHost.ConnectionString(Database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id FROM trax.persisted_operation WHERE tenant_key = '' AND is_active "
                + "AND starts_with(id, @prefix) ORDER BY id",
            connection
        );
        command.Parameters.AddWithValue("prefix", TrainDocuments.ReservedIdPrefix);
        var ids = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetString(0));
        return ids;
    }
}
