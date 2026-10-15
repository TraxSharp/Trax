using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Configuration.ClientExport;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// Builds and writes a host's <see cref="ClientManifest"/> from the schema it serves, with the code that registers its
/// documents, so every id in it is one the host serves. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
internal static class ClientManifestExport
{
    private const string TraxSchemaName = "trax";

    /// <summary>
    /// Runs every <see cref="ITraxStartupCheck"/> the host registers, then writes its manifest to
    /// <paramref name="path"/>. Starts no other hosted service, so nothing listens, sweeps or opens a database.
    /// </summary>
    public static async Task WriteAsync(
        IServiceProvider services,
        string path,
        CancellationToken cancellationToken
    )
    {
        foreach (var check in services.GetServices<IHostedService>().OfType<ITraxStartupCheck>())
        {
            if (check is IHostedLifecycleService lifecycle)
                await lifecycle.StartingAsync(cancellationToken).ConfigureAwait(false);
            else
                await ((IHostedService)check).StartAsync(cancellationToken).ConfigureAwait(false);
        }

        var executor = await services
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync(TraxSchemaName, cancellationToken)
            .ConfigureAwait(false);
        var exposed = MachineSurface.Exposed(
            services.GetServices<IMachine>(),
            services.GetService<StateMachineOptions>()
        );
        var trains = services.GetService<ITrainDiscoveryService>() is { } discovery
            ? TrainTypeModule.AssignTrainNames(discovery.DiscoverTrains())
            : new Dictionary<TrainRegistration, string>();
        var manifest = Build(executor.Schema, exposed, trains);

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (directory is not null)
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, ClientManifest.Write(manifest), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The manifest of <paramref name="exposed"/> machines and the trains in <paramref name="trains"/>.</summary>
    public static ClientManifest Build(
        ISchemaDefinition schema,
        IReadOnlyList<IMachine> exposed,
        IReadOnlyDictionary<TrainRegistration, string> trains
    )
    {
        var documents = exposed.Count == 0 ? [] : MachineDocuments.Generate(schema, exposed);
        var trainDocuments = TrainDocuments.Generate(schema, trains);
        var progressDocuments = ProgressDocuments.Generate(schema);
        var machines = exposed
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .Select(machine => new ClientManifestMachine
            {
                Id = machine.Name,
                IrHash = machine.SchemaHash!,
                Ir = (JsonObject)JsonNode.Parse(machine.ExportIr())!,
                Documents = documents
                    .Where(d => d.Machine == machine.Name)
                    .OrderBy(d => d.Operation, StringComparer.Ordinal)
                    .Select(d => new ClientManifestDocument
                    {
                        Id = d.Id,
                        Operation = d.Operation,
                        Document = d.Text,
                    })
                    .ToList(),
            })
            .ToList();

        return new ClientManifest
        {
            Machines = machines,
            Trains = trainDocuments
                .Select(d => new ClientManifestTrain
                {
                    Field = d.Field,
                    Root = d.Root,
                    Namespace = d.Namespace,
                    Answer = d.Answer,
                    Requires = RequirementOf(d.Registration),
                    Progress = progressDocuments.Count > 0 && d.Registration.IsRunProgressEnabled,
                    Documents =
                    [
                        new ClientManifestDocument
                        {
                            Id = d.Id,
                            Operation = d.Field,
                            Document = d.Text,
                        },
                    ],
                })
                .ToList(),
            Progress = progressDocuments
                .Select(d => new ClientManifestDocument
                {
                    Id = d.Id,
                    Operation = d.Operation,
                    Document = d.Text,
                })
                .ToList(),
            Types = DocumentTypes.Sdl(
                schema,
                documents
                    .Select(d => d.Text)
                    .Concat(trainDocuments.Select(d => d.Text))
                    .Concat(progressDocuments.Select(d => d.Text))
            ),
        };
    }

    private static ClientManifestRequirement RequirementOf(TrainRegistration train) =>
        new()
        {
            Anonymous = train.HasAllowAnonymousAttribute,
            RoleSets = train
                .RequiredRoleSets.Select(s => (IReadOnlyList<string>)s.ToList())
                .ToList(),
            Policies = train.RequiredPolicies.ToList(),
        };
}
