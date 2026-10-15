using AwesomeAssertions;
using Trax.Effect.StateMachine;
using Trax.Samples.Recovery.E2E.Factories;
using Trax.Samples.Recovery.E2E.Utilities;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The Recovery host started with <c>--trax-export-client</c> writes its client manifest and exits, in the production
/// configuration, with a database it cannot reach or with no connection string at all, so a frontend's build needs no
/// running server, no database and no credential. The ids it writes are the ones the host serves.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[TestFixture]
public class ClientManifestExportTests
{
    // Nothing listens on port 1, so a host that opened the database would fail its startup.
    private const string Unreachable =
        "Host=127.0.0.1;Port=1;Database=nowhere;Username=nobody;Password=nothing;Timeout=3";

    private string _folder = null!;

    [SetUp]
    public void Folder() =>
        _folder = Path.Combine(Path.GetTempPath(), $"recovery-manifest-{Guid.NewGuid():N}");

    [TearDown]
    public void Remove()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public async Task The_host_writes_its_manifest_and_exits_without_a_database()
    {
        var path = Path.Combine(_folder, "client.json");

        var (exitCode, output) = await RecoveryHostProcess.ExportAsync(
            path,
            Unreachable,
            TimeSpan.FromMinutes(2)
        );

        exitCode.Should().Be(0, output);
        output.Should().NotContain("Now listening on", "an export serves nothing");
        var manifest = ClientManifest.Read(await File.ReadAllTextAsync(path));
        manifest.Machines.Select(m => m.Id).Should().Contain(["order", "topic-map"]);
        manifest
            .Types.Should()
            .NotContainAny(
                ["type OperationsQueries", "type OperationsMutations", "operations:"],
                "the operations namespace stays on the server; a description may name it"
            );
    }

    [Test]
    public async Task The_host_writes_its_manifest_without_a_connection_string()
    {
        var path = Path.Combine(_folder, "client.json");

        var (exitCode, output) = await RecoveryHostProcess.ExportAsync(
            path,
            connectionString: null,
            TimeSpan.FromMinutes(2)
        );

        exitCode.Should().Be(0, output);
        ClientManifest
            .Read(await File.ReadAllTextAsync(path))
            .Machines.Select(m => m.Id)
            .Should()
            .Contain(["order", "topic-map"]);
    }

    [Test]
    public async Task The_committed_client_manifest_is_the_one_the_host_exports()
    {
        var path = Path.Combine(_folder, "client.json");
        var (exitCode, output) = await RecoveryHostProcess.ExportAsync(
            path,
            Unreachable,
            TimeSpan.FromMinutes(2)
        );
        exitCode.Should().Be(0, output);

        (await File.ReadAllTextAsync(path))
            .Should()
            .Be(
                (await File.ReadAllTextAsync(TopicMapArtifacts.ClientManifest)).Replace(
                    "\r\n",
                    "\n"
                ),
                "the page's generated client is written from this manifest; after changing a machine or a page "
                    + "train, export it again and run trax client (the sample's README)"
            );
    }
}
