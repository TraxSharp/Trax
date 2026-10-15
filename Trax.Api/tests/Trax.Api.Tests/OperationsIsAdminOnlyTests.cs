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
/// The dashboard is the React app Trax.Api.Dashboard ships, the one that speaks GraphQL; the Blazor dashboard calls the
/// same services directly. Enforces <c>Trax.Api/docs/adr/0041-operations-holds-only-what-the-dashboard-does.md</c>.
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
    internal static HashSet<string> DashboardSelections(ISchemaDefinition schema)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in DashboardDocuments())
        {
            var document = Utf8GraphQLParser.Parse(text);
            foreach (var operation in document.Definitions.OfType<OperationDefinitionNode>())
            {
                var root = operation.Operation switch
                {
                    OperationType.Query => schema.QueryType,
                    OperationType.Mutation => schema.MutationType,
                    _ => null,
                };
                if (root is null)
                    continue;
                foreach (var selection in Fields(operation.SelectionSet))
                    if (
                        selection.Name.Value == "operations"
                        && root.Fields.TryGetField("operations", out var operations)
                        && selection.SelectionSet is { } under
                    )
                        Walk((IObjectTypeDefinition)operations.Type.NamedType(), under, used);
            }
        }
        return used;
    }

    private static void Walk(
        IObjectTypeDefinition type,
        SelectionSetNode selections,
        HashSet<string> used
    )
    {
        foreach (var selection in Fields(selections))
        {
            if (!type.Fields.TryGetField(selection.Name.Value, out var field))
                continue;
            if (
                NamespaceField.IsNamespace(field)
                && field.Type.NamedType() is IObjectTypeDefinition inner
                && selection.SelectionSet is { } under
            )
                Walk(inner, under, used);
            else
                used.Add($"{type.Name}.{field.Name}");
        }
    }

    private static IEnumerable<FieldNode> Fields(SelectionSetNode set) =>
        set.Selections.SelectMany(s =>
            s switch
            {
                FieldNode f => [f],
                InlineFragmentNode i => Fields(i.SelectionSet),
                _ => Enumerable.Empty<FieldNode>(),
            }
        );

    private static IEnumerable<IObjectTypeDefinition> Roots(ISchemaDefinition schema)
    {
        yield return schema.QueryType;
        if (schema.MutationType is { } mutation)
            yield return mutation;
    }

    // The gql`...` documents in the dashboard's graphql folder. An interpolated constant is a piece of a selection set,
    // never an operations field, so it is read as __typename.
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
            .Select(t => Interpolation.Replace(t, "__typename"))
            .Where(t => Regex.IsMatch(t, @"^\s*(query|mutation|subscription)\b"))
            .ToList();
        documents.Should().NotBeEmpty($"the dashboard's documents are read from {folder}");
        return documents;
    }

    private static readonly Regex GqlTemplate = new(@"gql`([^`]*)`", RegexOptions.Compiled);
    private static readonly Regex Interpolation = new(@"\$\{[^}]*\}", RegexOptions.Compiled);

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
