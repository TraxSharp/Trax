using System.Text.RegularExpressions;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.Validation;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// Every field under <c>operations</c> is something the operations dashboard does: the guard reads the dashboard's
/// GraphQL documents, collects every field they select under <c>operations</c>, and fails on any field of the
/// namespace no document selects. A new field therefore has to be a dashboard action or read, or say which other
/// surface it belongs to.
/// </summary>
/// <remarks>
/// The dashboard is the operations console, the React app Trax.Api.Dashboard ships. The guard counts a document the
/// console defines, not one a page sends, so a document no page imports keeps its fields here until it is deleted.
/// Enforces <c>Trax.Api/docs/adr/0041-operations-holds-only-what-the-dashboard-does.md</c>.
/// </remarks>
[TestFixture]
[Property("adr", "docs/adr/0041-operations-holds-only-what-the-dashboard-does.md")]
public class OperationsIsAdminOnlyTests
{
    private const string Adr = "docs/adr/0041-operations-holds-only-what-the-dashboard-does.md";

    /// <summary>
    /// Fields under <c>operations</c> that no dashboard document selects, each with the reason it is still there. Each
    /// entry is temporary: the change that moves or removes the field removes its line.
    /// </summary>
    private static readonly Dictionary<string, string> NotADashboardAction = new(
        StringComparer.Ordinal
    );

    [Test]
    public async Task Every_operations_field_is_selected_by_a_dashboard_document()
    {
        var schema = await FullSchemaAsync();
        var served = OperationsFields(schema);
        var used = DashboardSelections(schema);

        var unused = served
            .Where(f => !used.Contains(f) && !NotADashboardAction.ContainsKey(f))
            .Order(StringComparer.Ordinal)
            .ToList();

        string.Join(", ", unused)
            .Should()
            .BeEmpty(
                "every field under operations is an operator's dashboard action or read, per "
                    + Adr
                    + ". A field no dashboard document selects belongs to another surface with a gate of its own "
                    + "(a deploy pipeline's, a service's, a monitor's), or the dashboard has to use it"
            );
    }

    [Test]
    public async Task Every_listed_exception_is_still_an_unused_operations_field()
    {
        var schema = await FullSchemaAsync();
        var served = OperationsFields(schema);
        var used = DashboardSelections(schema);

        string.Join(
                ", ",
                NotADashboardAction.Keys.Where(f => !served.Contains(f) || used.Contains(f))
            )
            .Should()
            .BeEmpty(
                "an exception is removed with the field it excuses, or once the dashboard uses it, per "
                    + Adr
            );
    }

    [Test]
    public async Task The_guard_reads_the_dashboards_documents()
    {
        var used = DashboardSelections(await FullSchemaAsync());

        used.Should()
            .Contain("OperationsQueries.executions")
            .And.Contain(
                "OperationsMutations.cancelExecution",
                "the guard would pass vacuously on an empty read"
            );
    }

    [Test]
    public async Task Every_dashboard_operations_selection_exists_in_the_schema()
    {
        var (_, missing) = ReadDashboard(await FullSchemaAsync());

        string.Join(", ", missing)
            .Should()
            .BeEmpty(
                "a dashboard document that selects an operations field the schema does not have calls a removed "
                    + "read or action, and the guard above would skip it rather than see it"
            );
    }

    [Test]
    public async Task A_selection_made_through_a_named_fragment_is_read()
    {
        var schema = await FullSchemaAsync();

        // The fragment defined in the same document, and in a document of its own that another
        // interpolates, as gql`... ${FIELDS}` does.
        var (_, sameDocument) = ReadDocuments(
            schema,
            [
                """
                query Listed { operations { ...Removed } }
                fragment Removed on OperationsQueries { noSuchRead }
                """,
            ]
        );
        var (_, ownDocument) = ReadDocuments(
            schema,
            [
                "query Listed { operations { ...Removed } } ${REMOVED}",
                "fragment Removed on OperationsQueries { executions { totalCount } noSuchRead }",
            ]
        );
        var (used, unknown) = ReadDocuments(
            schema,
            ["query Listed { operations { executions { totalCount } ...NotDefined } }"]
        );

        sameDocument.Should().Equal(["Listed: operations.noSuchRead"]);
        ownDocument.Should().Equal(["Listed: operations.noSuchRead"]);
        used.Should().Contain("OperationsQueries.executions");
        unknown.Should().Equal(["Listed: operations...NotDefined (no such fragment)"]);
    }

    // Every field reachable under operations, query and mutation, as Type.field; a nested namespace counts as its
    // fields, not as itself.
    internal static HashSet<string> OperationsFields(ISchemaDefinition schema)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in Roots(schema))
            if (root.Fields.TryGetField("operations", out var operations))
                Collect((IObjectTypeDefinition)operations.Type.NamedType(), fields, []);
        return fields;
    }

    private static void Collect(
        IObjectTypeDefinition type,
        HashSet<string> fields,
        HashSet<string> seen
    )
    {
        if (!seen.Add(type.Name))
            return;
        foreach (var field in type.Fields)
        {
            if (field.Name.StartsWith("__", StringComparison.Ordinal))
                continue;
            if (
                NamespaceField.IsNamespace(field)
                && field.Type.NamedType() is IObjectTypeDefinition inner
            )
                Collect(inner, fields, seen);
            else
                fields.Add($"{type.Name}.{field.Name}");
        }
    }

    // Every operations field the dashboard's documents select, resolved against the schema.
    internal static HashSet<string> DashboardSelections(ISchemaDefinition schema) =>
        ReadDashboard(schema).Used;

    // What the dashboard's documents select under operations: the fields the schema has, as Type.field, and the
    // selections it does not, each naming its document and path.
    private static (HashSet<string> Used, List<string> Missing) ReadDashboard(
        ISchemaDefinition schema
    ) => ReadDocuments(schema, DashboardDocuments());

    // A spread is read as the selections of the fragment it names: one defined in the same document, or in another
    // document, which gql`... ${FRAGMENT}` interpolates.
    private static (HashSet<string> Used, List<string> Missing) ReadDocuments(
        ISchemaDefinition schema,
        IEnumerable<string> texts
    )
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<string>();
        var documents = texts
            .Select(t => Utf8GraphQLParser.Parse(WithoutInterpolations(t)))
            .ToList();
        var shared = new Dictionary<string, FragmentDefinitionNode>(StringComparer.Ordinal);
        foreach (
            var fragment in documents.SelectMany(d =>
                d.Definitions.OfType<FragmentDefinitionNode>()
            )
        )
            shared.TryAdd(fragment.Name.Value, fragment);

        foreach (var document in documents)
        {
            var fragments = new Dictionary<string, FragmentDefinitionNode>(
                shared,
                StringComparer.Ordinal
            );
            foreach (var fragment in document.Definitions.OfType<FragmentDefinitionNode>())
                fragments[fragment.Name.Value] = fragment;

            foreach (var operation in document.Definitions.OfType<OperationDefinitionNode>())
            {
                var root = operation.Operation switch
                {
                    OperationType.Query => schema.QueryType,
                    OperationType.Mutation => schema.MutationType,
                    _ => null,
                };
                var name = operation.Name?.Value ?? "(anonymous)";
                foreach (var selection in Fields(operation.SelectionSet, fragments, missing, name))
                {
                    if (selection.Name.Value != "operations")
                        continue;
                    if (root is null || !root.Fields.TryGetField("operations", out var operations))
                    {
                        missing.Add($"{name}: operations");
                        continue;
                    }
                    if (selection.SelectionSet is { } under)
                        Walk(
                            (IObjectTypeDefinition)operations.Type.NamedType(),
                            under,
                            fragments,
                            used,
                            missing,
                            $"{name}: operations"
                        );
                }
            }
        }
        return (used, missing);
    }

    private static void Walk(
        IObjectTypeDefinition type,
        SelectionSetNode selections,
        IReadOnlyDictionary<string, FragmentDefinitionNode> fragments,
        HashSet<string> used,
        List<string> missing,
        string path
    )
    {
        foreach (var selection in Fields(selections, fragments, missing, path))
        {
            if (!type.Fields.TryGetField(selection.Name.Value, out var field))
            {
                missing.Add($"{path}.{selection.Name.Value}");
                continue;
            }
            if (
                NamespaceField.IsNamespace(field)
                && field.Type.NamedType() is IObjectTypeDefinition inner
                && selection.SelectionSet is { } under
            )
                Walk(inner, under, fragments, used, missing, $"{path}.{field.Name}");
            else
                used.Add($"{type.Name}.{field.Name}");
        }
    }

    // The fields a selection set selects, through inline fragments and named spreads. A spread of a fragment no
    // document defines is reported as missing.
    private static IEnumerable<FieldNode> Fields(
        SelectionSetNode set,
        IReadOnlyDictionary<string, FragmentDefinitionNode> fragments,
        List<string> missing,
        string path,
        HashSet<string>? spreading = null
    )
    {
        foreach (var selection in set.Selections)
        {
            switch (selection)
            {
                case FieldNode field:
                    yield return field;
                    break;
                case InlineFragmentNode inline:
                    foreach (
                        var inlined in Fields(
                            inline.SelectionSet,
                            fragments,
                            missing,
                            path,
                            spreading
                        )
                    )
                        yield return inlined;
                    break;
                case FragmentSpreadNode spread:
                    var name = spread.Name.Value;
                    if (!fragments.TryGetValue(name, out var fragment))
                    {
                        missing.Add($"{path}...{name} (no such fragment)");
                        break;
                    }
                    // A fragment that spreads itself is invalid GraphQL; read it once rather than forever.
                    var inside = new HashSet<string>(spreading ?? [], StringComparer.Ordinal);
                    if (!inside.Add(name))
                        break;
                    foreach (
                        var spreadField in Fields(
                            fragment.SelectionSet,
                            fragments,
                            missing,
                            path,
                            inside
                        )
                    )
                        yield return spreadField;
                    break;
            }
        }
    }

    // An interpolation inside a selection set is a piece of one, never an operations field, so it is read as
    // __typename; one outside every selection set appends a fragment definition, which is read from its own document.
    private static string WithoutInterpolations(string text)
    {
        var result = new System.Text.StringBuilder(text.Length);
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '$' && i + 1 < text.Length && text[i + 1] == '{')
            {
                var end = text.IndexOf('}', i);
                if (end < 0)
                    break;
                result.Append(depth > 0 ? "__typename" : "");
                i = end;
                continue;
            }
            if (text[i] == '{')
                depth++;
            else if (text[i] == '}')
                depth--;
            result.Append(text[i]);
        }
        return result.ToString();
    }

    private static IEnumerable<IObjectTypeDefinition> Roots(ISchemaDefinition schema)
    {
        yield return schema.QueryType;
        if (schema.MutationType is { } mutation)
            yield return mutation;
    }

    // The gql`...` documents in the dashboard's graphql folder: its operations, and the fragments they spread.
    private static IEnumerable<string> DashboardDocuments()
    {
        var folder = System.IO.Path.Combine(
            RepoFolder("Trax.Api"),
            "src",
            "Trax.Api.Dashboard",
            "ClientApp",
            "src",
            "graphql"
        );
        var documents = Directory
            .EnumerateFiles(folder, "*.ts")
            .Where(f => !f.EndsWith(".test.ts", StringComparison.Ordinal))
            .SelectMany(f =>
                GqlTemplate.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)
            )
            .Where(t => Regex.IsMatch(t, @"^\s*(query|mutation|subscription|fragment)\b"))
            .ToList();
        documents.Should().NotBeEmpty($"the dashboard's documents are read from {folder}");
        return documents;
    }

    private static readonly Regex GqlTemplate = new(@"gql`([^`]*)`", RegexOptions.Compiled);

    // The whole operations surface: both halves of the namespace, the machine instance feed it carries, and the
    // persisted-operation package, so a field it grafted onto operations would be caught.
    private static async Task<ISchemaDefinition> FullSchemaAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TraxMarker>();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        services.AddSingleton(Substitute.For<IDataContextProviderFactory>());
        services.AddTraxGraphQL(graphql =>
            graphql
                .ExposeOperationQueries()
                .ExposeOperationMutations()
                .GateOperations(roles: "Operator")
                .UsePersistedOperations(po => po.SingleNode().GateManagement(roles: "Operator"))
        );
        await using var provider = services.BuildServiceProvider();
        var executor = await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
        return executor.Schema;
    }

    private static string RepoFolder(string name)
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
        {
            var candidate = System.IO.Path.Combine(dir.FullName, name);
            if (
                Directory.Exists(candidate)
                && File.Exists(System.IO.Path.Combine(dir.FullName, "global.json"))
            )
                return candidate;
        }
        throw new DirectoryNotFoundException($"No {name} folder above {AppContext.BaseDirectory}.");
    }
}
