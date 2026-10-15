using System.Text.Json;
using AwesomeAssertions;
using Trax.Effect.StateMachine;
using Trax.Samples.Recovery.E2E.Factories;
using Trax.Samples.Recovery.E2E.Utilities;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The Recovery host started with <c>--trax-export-client</c> writes its client manifest and exits, in the production
/// configuration and with a database it cannot reach, so a frontend's build needs no running server, no database and
/// no credential. The ids it writes are the ones the host serves.
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
        manifest.Types.Should().NotContain("operations");
    }

    [Test]
    public async Task Its_topic_map_documents_are_the_ones_the_host_serves()
    {
        var path = Path.Combine(_folder, "client.json");
        var (exitCode, output) = await RecoveryHostProcess.ExportAsync(
            path,
            Unreachable,
            TimeSpan.FromMinutes(2)
        );
        exitCode.Should().Be(0, output);

        var exported = ClientManifest
            .Read(await File.ReadAllTextAsync(path))
            .Machines.Single(m => m.Id == "topic-map")
            .Documents.Select(d => d.Id)
            .Order(StringComparer.Ordinal);
        // The committed file holds the documents a running host registers (TopicMapDocumentsTests).
        var served = JsonDocument
            .Parse(await File.ReadAllTextAsync(TopicMapArtifacts.Documents))
            .RootElement.EnumerateArray()
            .Select(d => d.GetProperty("id").GetString()!)
            .Order(StringComparer.Ordinal);

        exported.Should().Equal(served);
    }
}
