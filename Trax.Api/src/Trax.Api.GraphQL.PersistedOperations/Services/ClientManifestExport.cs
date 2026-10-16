using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Configuration;
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
        var manifest = Build(
            executor.Schema,
            exposed,
            trains,
            services.GetService<GraphQLConfiguration>()?.ModelRegistrations ?? []
        );

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (directory is not null)
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, ClientManifest.Write(manifest), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The manifest of <paramref name="exposed"/> machines, the trains in <paramref name="trains"/> and the
    /// <paramref name="queryModels"/> the schema serves.
    /// </summary>
    public static ClientManifest Build(
        ISchemaDefinition schema,
        IReadOnlyList<IMachine> exposed,
        IReadOnlyDictionary<TrainRegistration, string> trains,
        IReadOnlyList<QueryModelRegistration>? queryModels = null
    )
    {
        var documents = exposed.Count == 0 ? [] : MachineDocuments.Generate(schema, exposed);
        var exposedTrains = ExposedTrains.Resolve(schema, trains);
        var models = (queryModels ?? [])
            .Select(r =>
            {
                var ns = r.Attribute.Namespace is { } n ? TrainTypeModule.CamelCase(n) : null;
                var field = QueryModelTypeModule.FieldName(r);
                return
                    ExposedTrains.FieldOf(schema, mutation: false, ns, field)
                        is var (holder, definition)
                    ? (
                        Registration: r,
                        Namespace: ns,
                        Field: field,
                        Holder: holder,
                        Definition: definition
                    )
                    : default;
            })
            .Where(m => m.Definition is not null)
            .OrderBy(m => m.Namespace ?? "", StringComparer.Ordinal)
            .ThenBy(m => m.Field, StringComparer.Ordinal)
            .ToList();
        var progressDocuments = ProgressDocuments.Generate(schema);
        GeneratedDocumentValidation.Validate(schema, [.. documents, .. progressDocuments]);
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

        var types = DocumentTypes.Sdl(
            schema,
            documents.Select(d => d.Text).Concat(progressDocuments.Select(d => d.Text)),
            exposedTrains
                .Select(t => $"{t.Holder}.{t.Field}")
                .Concat(models.Select(m => $"{m.Holder}.{m.Field}"))
        );
        return new ClientManifest
        {
            Machines = machines,
            Trains = exposedTrains
                .Select(t => new ClientManifestTrain
                {
                    Field = t.Field,
                    Root = t.Root,
                    Namespace = t.Namespace,
                    Answer = Print(t.Definition.Type),
                    Arguments = t.Definition.Arguments.Select(Argument).ToList(),
                    Requires = RequirementOf(t.Registration),
                    Progress = progressDocuments.Count > 0 && t.Registration.IsRunProgressEnabled,
                })
                .ToList(),
            QueryModels = models
                .Select(m => new ClientManifestQueryModel
                {
                    Field = m.Field,
                    Namespace = m.Namespace,
                    Answer = Print(m.Definition.Type),
                    Arguments = m.Definition.Arguments.Select(Argument).ToList(),
                    Requires = RequirementOf(m.Registration),
                })
                .ToList(),
            FilterInputs = FilterInputs(schema, types),
            Progress = progressDocuments
                .Select(d => new ClientManifestDocument
                {
                    Id = d.Id,
                    Operation = d.Operation,
                    Document = d.Text,
                })
                .ToList(),
            // Every type a train's or query model's arguments and answer reach, in full, so a client document may
            // select any of it.
            Types = types,
        };
    }

    private static ClientManifestArgument Argument(IInputValueDefinition argument) =>
        new()
        {
            Name = argument.Name,
            Type = Print(argument.Type),
            Default = argument.DefaultValue?.ToString(),
        };

    // The listed types that are filter or sort inputs, in name order.
    private static List<string> FilterInputs(ISchemaDefinition schema, string sdl) =>
        Utf8GraphQLParser
            .Parse(string.IsNullOrWhiteSpace(sdl) ? "scalar _None" : sdl)
            .Definitions.OfType<INamedSyntaxNode>()
            .Select(d => d.Name.Value)
            .Where(name =>
                schema.Types.TryGetType(name, out var type)
                && type is IFilterInputType or ISortInputType
            )
            .Order(StringComparer.Ordinal)
            .ToList();

    private static ClientManifestRequirement RequirementOf(QueryModelRegistration model) =>
        new()
        {
            Anonymous = model.AllowAnonymous,
            RoleSets = model
                .AuthorizeAttributes.Where(a => !string.IsNullOrWhiteSpace(a.Roles))
                .Select(a =>
                    (IReadOnlyList<string>)
                        a.Roles!.Split(
                                ',',
                                StringSplitOptions.TrimEntries
                                    | StringSplitOptions.RemoveEmptyEntries
                            )
                            .ToList()
                )
                .ToList(),
            Policies = model
                .AuthorizeAttributes.Select(a => a.Policy)
                .OfType<string>()
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
        };

    private static string Print(IType type) =>
        type.Kind switch
        {
            TypeKind.NonNull => Print(type.InnerType()) + "!",
            TypeKind.List => "[" + Print(type.InnerType()) + "]",
            _ => type.NamedType().Name,
        };

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
