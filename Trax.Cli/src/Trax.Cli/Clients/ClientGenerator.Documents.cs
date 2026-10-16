using System.Text;
using System.Text.Json;
using GraphQLParser.AST;
using Trax.Effect.StateMachine;

namespace Trax.Cli.Clients;

internal sealed partial class ClientGenerator
{
    /// <summary>The file the host names in <c>ClientDocuments(path)</c>, written under the output directory.</summary>
    public const string DocumentsFileName = "trax.documents.json";

    /// <summary>
    /// The schema written for the editor, under the output directory. It is a <c>.graphql</c> file a document glob may
    /// match, so <c>--documents</c> never reads it.
    /// </summary>
    public const string EditorSchemaFileName = "schema.graphql";

    // The root and namespace types the editor's schema declares, which the manifest leaves out.
    private const string EditorQuery = "TraxQuery";
    private const string EditorMutation = "TraxMutation";

    private static string Lower(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    // A client document's module: its id, the type of what it selects, and the function that sends it.
    private static string DocumentModule(
        ClientOperation operation,
        string function,
        string result,
        SdlTypes types,
        ClientDocumentSet documents
    )
    {
        var owner = $"The client document {operation.Name} ({operation.Source})";
        var selections = new SelectionTypes(
            types,
            documents.Fragments.ToDictionary(
                f => f.Key,
                f => f.Value.Definition,
                StringComparer.Ordinal
            )
        );
        var used = new HashSet<string>(StringComparer.Ordinal);
        string resultType;
        IReadOnlyList<string> path;
        if (operation.Trains is [var single])
        {
            resultType = selections.Of(
                SdlTypes.ParseType(single.Train.Answer, $"the train '{single.Train.Field}'")!,
                single.Field.SelectionSet,
                owner,
                used
            );
            path = single.Path;
        }
        else
        {
            resultType = Several(operation, selections, owner, used);
            path = [operation.Root];
        }

        var variables = operation.Operation.Variables?.Items ?? [];
        var (parameters, values) = Signature(
            variables,
            types,
            function,
            owner,
            "Rename the variable in the document."
        );
        var imports = types
            .NamesIn(variables.Select(v => v.Type))
            .Concat(used)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder(Header);
        sb.Append("import \"../format.g\";\n");
        sb.Append("import { sendDocument } from \"../client.g\";\n");
        if (imports.Count > 0)
            sb.Append($"import type {{ {string.Join(", ", imports)} }} from \"../types.g\";\n");
        sb.Append('\n');
        sb.Append(
            $"/** The persisted id of the client document '{EscapeDocText(operation.Name)}' ({EscapeDocText(operation.Source)}), which the host registers from {DocumentsFileName}. */\n"
        );
        sb.Append($"export const {function}DocumentId = {TypeScript.Literal(operation.Id)};\n\n");
        sb.Append(
            $"/** What '{EscapeDocText(operation.Name)}' answers: the fields it selects. */\n"
        );
        sb.Append($"export type {result} = {resultType};\n\n");
        var requirements = operation
            .Trains.Select(t => Requirement(t.Train.Requires))
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (requirements.Count > 0)
            sb.Append(TypeScript.Doc(string.Join(" ", requirements)));
        sb.Append(
            $"export function {function}({string.Join(", ", parameters)}): Promise<{result}> {{\n"
        );
        sb.Append(
            $"  return sendDocument({function}DocumentId, {(values.Count == 0 ? "{}" : $"{{ {string.Join(", ", values)} }}")}, [{string.Join(", ", path.Select(TypeScript.Literal))}]);\n"
        );
        sb.Append("}\n");
        return sb.ToString();
    }

    // What a query of several trains answers: each train under its key, inside its namespace when it has one.
    private static string Several(
        ClientOperation operation,
        SelectionTypes selections,
        string owner,
        ISet<string> used
    )
    {
        var sb = new StringBuilder("{\n");
        foreach (var group in operation.Trains.GroupBy(t => t.Path.Count == 2 ? null : t.Path[1]))
        {
            var indent = group.Key is null ? 2 : 4;
            if (group.Key is not null)
                sb.Append($"  {SdlTypes.Property(group.Key)}: {{\n");
            foreach (var train in group)
                sb.Append(new string(' ', indent))
                    .Append(SdlTypes.Property(train.Path[^1]))
                    .Append(": ")
                    .Append(
                        selections.Of(
                            SdlTypes.ParseType(
                                train.Train.Answer,
                                $"the train '{train.Train.Field}'"
                            )!,
                            train.Field.SelectionSet,
                            owner,
                            used,
                            indent
                        )
                    )
                    .Append(";\n");
            if (group.Key is not null)
                sb.Append("  };\n");
        }
        return sb.Append('}').ToString();
    }

    // Each named fragment as a type of its own, for a component that takes that shape.
    private static string FragmentsModule(ClientDocumentSet documents, SdlTypes types)
    {
        var selections = new SelectionTypes(
            types,
            documents.Fragments.ToDictionary(
                f => f.Key,
                f => f.Value.Definition,
                StringComparer.Ordinal
            )
        );
        var used = new HashSet<string>(StringComparer.Ordinal);
        var body = new StringBuilder();
        foreach (
            var (name, (source, definition)) in documents.Fragments.OrderBy(
                f => f.Key,
                StringComparer.Ordinal
            )
        )
        {
            var on = definition.TypeCondition.Type.Name.StringValue;
            body.Append(
                $"/** The fragment '{EscapeDocText(name)}' on {EscapeDocText(on)} ({EscapeDocText(source)}): the fields it selects. */\n"
            );
            body.Append(
                $"export type {name} = {selections.Of(SdlTypes.ParseType(on + "!", $"the fragment '{name}'")!, definition.SelectionSet, $"the fragment {name}", used)};\n\n"
            );
        }
        var sb = new StringBuilder(Header).Append("import \"./format.g\";\n");
        if (used.Count > 0)
            sb.Append(
                $"import type {{ {string.Join(", ", used.Order(StringComparer.Ordinal))} }} from \"./types.g\";\n"
            );
        sb.Append('\n').Append(body.ToString().TrimEnd('\n')).Append('\n');
        return sb.ToString();
    }

    // The file the host registers the documents from: each operation's text with the fragments it spreads, by name.
    private static string DocumentsJson(ClientDocumentSet documents) =>
        JsonSerializer
            .Serialize(
                new
                {
                    format = 1,
                    documents = documents.Operations.Select(o => new
                    {
                        name = o.Name,
                        source = o.Source,
                        text = o.Text,
                    }),
                },
                new JsonSerializerOptions { WriteIndented = true }
            )
            .Replace("\r\n", "\n") + "\n";

    // The manifest's types plus the root and namespace types that hold each exposed train, so an editor checks and
    // completes a document against what the host serves.
    private static string EditorSchema(ClientManifest manifest)
    {
        var sb = new StringBuilder(
            "# Generated by trax client from the host's client manifest, for the GraphQL editor. Do not edit.\n\n"
        );
        var roots = new List<(string Kind, string Type)>();
        foreach (
            var (kind, rootField, rootType) in new[]
            {
                ("query", "discover", EditorQuery),
                ("mutation", "dispatch", EditorMutation),
            }
        )
        {
            var trains = manifest.Trains.Where(t => t.Root == kind).ToList();
            if (trains.Count == 0)
                continue;
            roots.Add((kind, rootType));
            var holder = rootType + TypeScript.Upper(rootField);
            sb.Append($"type {rootType} {{\n  {rootField}: {holder}\n}}\n\n");
            sb.Append($"type {holder} {{\n");
            foreach (
                var train in trains
                    .Where(t => t.Namespace is null)
                    .OrderBy(t => t.Field, StringComparer.Ordinal)
            )
                sb.Append($"  {TrainField(train)}\n");
            var namespaces = trains
                .Where(t => t.Namespace is not null)
                .Select(t => t.Namespace!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
            foreach (var ns in namespaces)
                sb.Append($"  {ns}: {holder}{TypeScript.Upper(ns)}\n");
            sb.Append("}\n\n");
            foreach (var ns in namespaces)
            {
                sb.Append($"type {holder}{TypeScript.Upper(ns)} {{\n");
                foreach (
                    var train in trains
                        .Where(t => t.Namespace == ns)
                        .OrderBy(t => t.Field, StringComparer.Ordinal)
                )
                    sb.Append($"  {TrainField(train)}\n");
                sb.Append("}\n\n");
            }
        }
        sb.Append("schema {\n");
        foreach (var (kind, type) in roots)
            sb.Append($"  {kind}: {type}\n");
        sb.Append("}\n\n");
        sb.Append(WithoutDirectives((manifest.Types ?? "").Replace("\r\n", "\n").Trim()))
            .Append('\n');
        return sb.ToString();
    }

    // The manifest's types without the directives the host's schema puts on them (`@cost`), which the editor's schema
    // does not declare. A description sits on a line of its own, so only the other lines are touched.
    private static string WithoutDirectives(string sdl) =>
        string.Join(
            '\n',
            sdl.Split('\n')
                .Select(line =>
                    line.TrimStart().StartsWith('"') ? line : DirectiveUse().Replace(line, "")
                )
        );

    [System.Text.RegularExpressions.GeneratedRegex(
        @"\s+@[_A-Za-z][_0-9A-Za-z]*(?:\((?:[^()""]|""(?:[^""\\]|\\.)*"")*\))?"
    )]
    private static partial System.Text.RegularExpressions.Regex DirectiveUse();

    private static string TrainField(ClientManifestTrain train)
    {
        var arguments = ClientDocumentSet.TrainArguments(train);
        var args =
            arguments.Count == 0
                ? ""
                : "("
                    + string.Join(", ", arguments.Select(a => $"{a.Key}: {PrintType(a.Value)}"))
                    + ")";
        return $"{train.Field}{args}: {train.Answer}";
    }

    private static string PrintType(GraphQLType type) =>
        type switch
        {
            GraphQLNonNullType n => PrintType(n.Type) + "!",
            GraphQLListType l => "[" + PrintType(l.Type) + "]",
            GraphQLNamedType named => named.Name.StringValue,
            _ => "",
        };

    private static string EditorConfig(IReadOnlyList<string> globs)
    {
        var sb = new StringBuilder(
            "# Generated by trax client, for the GraphQL editor extension. Do not edit.\n"
        );
        sb.Append($"schema: {EditorSchemaFileName}\n");
        sb.Append("documents:\n");
        foreach (var glob in globs)
            sb.Append($"  - {TypeScript.Literal(glob)}\n");
        sb.Append($"exclude:\n  - {EditorSchemaFileName}\n");
        return sb.ToString();
    }
}
