using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Configuration.ClientExport;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

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
        var manifest = Build(executor.Schema, exposed);

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (directory is not null)
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, ClientManifest.Write(manifest), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The manifest of <paramref name="exposed"/> over <paramref name="schema"/>.</summary>
    public static ClientManifest Build(ISchemaDefinition schema, IReadOnlyList<IMachine> exposed)
    {
        var documents = exposed.Count == 0 ? [] : MachineDocuments.Generate(schema, exposed);
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
            Types = TypesOf(schema, documents.Select(d => d.Text)),
        };
    }

    // The SDL of every named type the documents reach: their variables' types with every input type those hold, and
    // the types of the fields they select, through every fragment. The root operation types are left out, so the
    // fields a client cannot send, the operations namespace among them, are never listed.
    private static string TypesOf(ISchemaDefinition schema, IEnumerable<string> documents)
    {
        var reached = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var text in documents)
        {
            var document = Utf8GraphQLParser.Parse(text);
            foreach (var operation in document.Definitions.OfType<OperationDefinitionNode>())
            {
                foreach (var variable in operation.VariableDefinitions)
                    Reach(schema, variable.Type.NamedType().Name.Value, reached);
                var root = operation.Operation switch
                {
                    OperationType.Mutation => schema.MutationType,
                    OperationType.Subscription => schema.SubscriptionType,
                    _ => schema.QueryType,
                };
                if (root is not null)
                    Select(schema, root, operation.SelectionSet, reached);
            }
        }

        var roots = new[] { schema.QueryType, schema.MutationType, schema.SubscriptionType }
            .Where(t => t is not null)
            .Select(t => t!.Name)
            .ToHashSet(StringComparer.Ordinal);
        var definitions = Utf8GraphQLParser
            .Parse(schema.ToString())
            .Definitions.OfType<INamedSyntaxNode>()
            .Where(d => reached.Contains(d.Name.Value) && !roots.Contains(d.Name.Value))
            .OrderBy(d => d.Name.Value, StringComparer.Ordinal)
            .Select(d => d.ToString(indented: true));
        return string.Join("\n\n", definitions).Replace("\r\n", "\n");
    }

    private static void Select(
        ISchemaDefinition schema,
        ITypeDefinition type,
        SelectionSetNode? selections,
        SortedSet<string> reached
    )
    {
        if (selections is null)
            return;
        foreach (var selection in selections.Selections)
        {
            switch (selection)
            {
                case FieldNode field
                    when type is IComplexTypeDefinition complex
                        && complex.Fields.TryGetField(field.Name.Value, out var definition):
                    var named = definition.Type.NamedType();
                    Reach(schema, named.Name, reached);
                    Select(schema, named, field.SelectionSet, reached);
                    break;
                case InlineFragmentNode fragment:
                    var on =
                        fragment.TypeCondition is { } condition
                        && schema.Types.TryGetType(condition.Name.Value, out var conditionType)
                            ? conditionType
                            : type;
                    Reach(schema, on.Name, reached);
                    Select(schema, on, fragment.SelectionSet, reached);
                    break;
            }
        }
    }

    // A named type, and every input type an input object holds.
    private static void Reach(ISchemaDefinition schema, string name, SortedSet<string> reached)
    {
        if (!reached.Add(name) || !schema.Types.TryGetType(name, out var type))
            return;
        if (type is IInputObjectTypeDefinition input)
            foreach (var field in input.Fields)
                Reach(schema, field.Type.NamedType().Name, reached);
    }
}
