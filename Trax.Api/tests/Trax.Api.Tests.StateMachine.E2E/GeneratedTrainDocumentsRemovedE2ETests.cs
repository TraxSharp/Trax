using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.StateMachine;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// A host generates no document for a train: one answering seven types that each list the others starts, a frontend
/// reaches it through a document it wrote, an id an earlier version generated under <c>trax_train_</c> is answered as
/// outdated, and the client manifest lists each train with its arguments and what it requires, and every type its
/// answer reaches.
/// <para>Enforces <c>Trax.Docs/adr/0054-a-frontend-writes-every-document-that-reaches-a-train.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0054-a-frontend-writes-every-document-that-reaches-a-train.md")]
[TestFixture]
[NonParallelizable]
public class GeneratedTrainDocumentsRemovedE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0054-a-frontend-writes-every-document-that-reaches-a-train.md";

    private const string Database = "trax_sm_train_documents";

    private const string MeshCard =
        "query MeshCard($input: ReadMeshInput!) { discover { readMesh(input: $input) { name mesh1s { name "
        + "mesh2s { name } } } } }";

    private const string OrderCard =
        "query OrderCard($input: FindOrderInput!) { discover { findOrder(input: $input) { title customer { name "
        + "orders { title } } } } }";

    private IHost _host = null!;
    private string _file = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        _file = Path.Combine(Path.GetTempPath(), $"trax-documents-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            _file,
            JsonSerializer.Serialize(
                new
                {
                    format = 1,
                    documents = new[] { ("MeshCard", MeshCard), ("OrderCard", OrderCard) }.Select(
                        d => new
                        {
                            name = d.Item1,
                            source = $"src/{d.Item1}.graphql",
                            text = d.Item2,
                        }
                    ),
                }
            )
        );
        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Served = [typeof(WizardMachine)],
                GraphQL = graphql =>
                    graphql.UsePersistedOperations(po =>
                        po.SingleNode().RequirePersisted(true).ClientDocuments(_file)
                    ),
            }
        );
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
        File.Delete(_file);
    }

    [Test]
    public async Task A_host_exposing_a_train_answering_a_mesh_starts_and_registers_no_train_document()
    {
        (await TrainIdsAsync())
            .Should()
            .BeEmpty("a host generates no document for a train, per " + Adr);
    }

    [Test]
    public async Task A_client_document_reaches_a_train_answering_a_mesh()
    {
        using var answer = await _host.PostPersistedAsync(
            ClientDocuments.IdOf("MeshCard", MeshCard),
            new { input = new { name = "zero" } }
        );

        var mesh = answer
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("readMesh");
        mesh.GetProperty("name").GetString().Should().Be("zero", MachineDocumentOps.Show(answer));
        mesh.GetProperty("mesh1s")[0]
            .GetProperty("mesh2s")[0]
            .GetProperty("name")
            .GetString()
            .Should()
            .Be("two");
    }

    [Test]
    public async Task A_client_document_follows_a_back_reference_the_frontend_chose()
    {
        using var answer = await _host.PostPersistedAsync(
            ClientDocuments.IdOf("OrderCard", OrderCard),
            new { input = new { id = 7 } }
        );

        answer
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("findOrder")
            .GetProperty("customer")
            .GetProperty("orders")[0]
            .GetProperty("title")
            .GetString()
            .Should()
            .Be("Order 7", MachineDocumentOps.Show(answer));
    }

    [Test]
    public async Task An_id_an_earlier_version_generated_for_a_train_is_answered_as_outdated()
    {
        var earlier =
            TrainDocuments.ReservedIdPrefix
            + "mutation__recordNote_"
            + new string('9', MachineDocuments.IrHashLength + MachineDocuments.TextHashLength);

        var (status, body) = await _host.PostPersistedWithStatusAsync(
            earlier,
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
                .BeInRange(
                    400,
                    499,
                    "a page built against a generated train function reloads, per " + Adr
                );
        }
    }

    [Test]
    public async Task The_manifest_lists_each_train_with_its_arguments_and_every_type_its_answer_reaches()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trax-trains-{Guid.NewGuid():N}.json");
        try
        {
            await ClientManifestExport.WriteAsync(_host.Services, path, CancellationToken.None);
            var text = await File.ReadAllTextAsync(path);
            var manifest = ClientManifest.Read(text);

            var record = manifest.Trains.Single(t => t.Field == "recordNote");
            record.Root.Should().Be("mutation");
            record.Requires!.RoleSets.Should().ContainSingle().Which.Should().Equal("Approver");
            record
                .Arguments.Should()
                .Contain(a => a.Name == "input" && a.Type == "RecordNoteInput!");
            manifest.Trains.Single(t => t.Field == "ping").Requires!.Anonymous.Should().BeTrue();
            text.Should().NotContain(TrainDocuments.ReservedIdPrefix);
            manifest
                .Types.Should()
                .Contain("input RecordNoteInput")
                .And.Contain("type Mesh6Output")
                .And.Contain("orders: [OrderOutput!]!", "a client may select the back-reference")
                .And.NotContain("operations");
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
            "SELECT id FROM trax.persisted_operation WHERE tenant_key = '' AND starts_with(id, @prefix)",
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
