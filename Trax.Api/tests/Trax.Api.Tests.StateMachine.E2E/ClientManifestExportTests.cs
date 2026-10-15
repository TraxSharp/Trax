using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.Configuration.ClientExport;
using Trax.Effect.StateMachine;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// A host writes the client manifest <c>trax client</c> reads from the code that registers its documents: every id in
/// it is one the host serves, each exposed machine carries its IR, the types it lists are only the ones the documents
/// reach (never a root type, so never the operations namespace), and a startup check that refuses the host refuses
/// the export.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class ClientManifestExportTests
{
    private const string Database = "trax_sm_client_manifest";

    private IHost _host = null!;
    private string _path = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Served = [typeof(WizardMachine), typeof(TicketMachine)],
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

    [SetUp]
    public void NewPath() =>
        _path = Path.Combine(
            Path.GetTempPath(),
            $"trax-manifest-{Guid.NewGuid():N}",
            "client.json"
        );

    [TearDown]
    public void RemovePath()
    {
        if (Directory.Exists(Path.GetDirectoryName(_path)))
            Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true);
    }

    private async Task<ClientManifest> ExportAsync()
    {
        await ClientManifestExport.WriteAsync(_host.Services, _path, CancellationToken.None);
        return ClientManifest.Read(await File.ReadAllTextAsync(_path));
    }

    [Test]
    public async Task Every_id_is_one_the_host_registers()
    {
        var manifest = await ExportAsync();
        var registered = (await MachineDocumentOps.MachineDocumentsAsync(Database))
            .Where(d => d.TenantKey == "" && d.IsActive)
            .Select(d => d.Id)
            .ToList();

        var exported = manifest.Machines.SelectMany(m => m.Documents).Select(d => d.Id).ToList();

        exported.Should().NotBeEmpty();
        exported
            .Should()
            .BeSubsetOf(
                registered,
                "the manifest is written by the code that registers the documents"
            );
        manifest
            .Machines.Select(m => m.Id)
            .Should()
            .Equal(TicketMachine.MachineId, WizardMachine.MachineId);
        manifest
            .Machines.Single(m => m.Id == WizardMachine.MachineId)
            .Documents.Select(d => d.Operation)
            .Should()
            .Contain(["start", "get", "discard", "changed", "describe"]);
    }

    [Test]
    public async Task Each_machine_carries_its_IR_and_hash()
    {
        var manifest = await ExportAsync();
        var wizard = manifest.Machines.Single(m => m.Id == WizardMachine.MachineId);

        wizard.Ir["id"]!.GetValue<string>().Should().Be(WizardMachine.MachineId);
        Convert
            .ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(wizard.IrText())
                )
            )
            .Should()
            .Be(
                wizard.IrHash,
                "a twin generated from the manifest's IR names the hash the documents carry"
            );
        wizard.Documents.Should().OnlyContain(d => d.Id.Contains(wizard.IrHash));
    }

    [Test]
    public async Task The_types_are_only_the_ones_the_documents_reach()
    {
        var manifest = await ExportAsync();

        manifest
            .Types.Should()
            .Contain("type WizardMachineResult")
            .And.Contain("enum TicketPriority");
        manifest.Types.Should().Contain("input WizardDetailsInput");
        manifest
            .Types.Should()
            .NotContain("type RootQuery")
            .And.NotContain("type RootMutation")
            .And.NotContain(
                "operations",
                "the operations namespace is never in a client's manifest"
            );
    }

    [Test]
    public async Task The_same_host_writes_the_same_bytes()
    {
        await ClientManifestExport.WriteAsync(_host.Services, _path, CancellationToken.None);
        var first = await File.ReadAllTextAsync(_path);
        await ClientManifestExport.WriteAsync(_host.Services, _path, CancellationToken.None);

        (await File.ReadAllTextAsync(_path)).Should().Be(first);
    }

    [Test]
    public async Task A_startup_check_that_refuses_the_host_refuses_the_export()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostedService, RefusingCheck>();
        await using var provider = services.BuildServiceProvider();

        var export = () => ClientManifestExport.WriteAsync(provider, _path, CancellationToken.None);

        await export.Should().ThrowAsync<InvalidOperationException>().WithMessage("refused");
        File.Exists(_path).Should().BeFalse("nothing is written for a host that would not start");
    }

    [Test]
    public async Task A_host_started_to_export_that_calls_Run_refuses_to_serve()
    {
        var database = Database + "_serve";
        await E2EHost.RecreateDatabaseAsync(database);
        try
        {
            // As UsePostgres does when the process was started with the flag: it skips the migration and registers
            // the gate. Starting the host is what app.Run() does.
            var start = () =>
                E2EHost.StartAsync(
                    database,
                    new CountingCharge(),
                    new E2EHostOptions
                    {
                        Served = [typeof(WizardMachine)],
                        GraphQL = MachineDocumentOps.Persisted(),
                        Services = services =>
                            TraxClientExport.SkipsDatabase(
                                services,
                                new ClientExportFlag(() =>
                                    ["host.dll", TraxClientExport.Argument, _path]
                                )
                            ),
                    }
                );

            await start
                .Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*--trax-export-client*not migrated*RunTraxAsync*");
        }
        finally
        {
            await E2EHost.DropDatabaseAsync(database);
        }
    }

    [TestCase(new string[0], null)]
    [TestCase(new[] { "--urls", "x" }, null)]
    [TestCase(new[] { "--trax-export-client", "out/client.json" }, "out/client.json")]
    [TestCase(new[] { "--trax-export-client=out/client.json" }, "out/client.json")]
    public void The_export_is_asked_for_by_its_argument(string[] args, string? path) =>
        TraxClientExport.PathIn(args).Should().Be(path);

    [Test]
    public void The_argument_without_a_path_is_refused()
    {
        var read = () => TraxClientExport.PathIn(["--trax-export-client"]);

        read.Should().Throw<ArgumentException>();
    }

    private sealed class RefusingCheck : IHostedLifecycleService, ITraxStartupCheck
    {
        public Task StartingAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("refused");

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
