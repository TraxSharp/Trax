using System.Security.Cryptography;
using System.Text;
using GraphQLParser;
using GraphQLParser.AST;
using GraphQLParser.Visitors;
using Trax.Effect.StateMachine;

namespace Trax.Cli.Clients;

/// <summary>One <c>.graphql</c> file a frontend wrote, by the path it is named under and its text.</summary>
internal sealed record ClientDocumentFile(string Source, string Text);

/// <summary>
/// A train or query model a client document selects: its manifest entry (a query model's written as a train's, under
/// the query root), its field, the path to its value, and whether it is a query model.
/// </summary>
internal sealed record ClientTrainUse(
    ClientManifestTrain Train,
    GraphQLField Field,
    IReadOnlyList<string> Path,
    bool QueryModel = false
);

/// <summary>One operation a frontend wrote, checked, with the text and id its host registers it under.</summary>
internal sealed record ClientOperation(
    string Name,
    string Source,
    GraphQLOperationDefinition Operation,
    string Root,
    IReadOnlyList<ClientTrainUse> Trains,
    string Text,
    string Id
);

/// <summary>
/// The <c>.graphql</c> files a frontend wrote, read and checked against the host's client manifest the way the host
/// checks them again at startup: one named query or mutation per file, its root <c>discover</c> or <c>dispatch</c>,
/// under it only the manifest's trains, its query models (in a query) and their namespaces, a mutation exactly one
/// train, no directive, no variable of a filter or sort input; every field, argument, variable and fragment as the
/// manifest's types allow. A file of fragments only holds named fragments any
/// operation may spread. Each refusal names the file, line and column.
/// </summary>
internal sealed class ClientDocumentSet
{
    private const string Prefix = "trax_client_";

    private ClientDocumentSet(
        IReadOnlyList<ClientOperation> operations,
        IReadOnlyDictionary<string, (string Source, GraphQLFragmentDefinition Definition)> fragments
    )
    {
        Operations = operations;
        Fragments = fragments;
    }

    /// <summary>The operations, in name order.</summary>
    public IReadOnlyList<ClientOperation> Operations { get; }

    /// <summary>Every named fragment, by name.</summary>
    public IReadOnlyDictionary<
        string,
        (string Source, GraphQLFragmentDefinition Definition)
    > Fragments { get; }

    /// <summary>The id the host registers <paramref name="text"/> under as the document <paramref name="name"/>.</summary>
    public static string IdOf(string name, string text)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var check = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(hash + "\n" + text))
        )[..16];
        return $"{Prefix}{name.Replace('_', '-')}_{hash}{check}";
    }

    /// <summary>Reads and checks <paramref name="files"/>; every refusal is reported at once.</summary>
    /// <exception cref="InvalidOperationException">A file breaks a rule.</exception>
    public static ClientDocumentSet Read(
        IReadOnlyList<ClientDocumentFile> files,
        ClientManifest manifest,
        SdlTypes types
    )
    {
        var errors = new List<string>();
        var parsed = new List<(ClientDocumentFile File, GraphQLDocument Document)>();
        foreach (var file in files.OrderBy(f => f.Source, StringComparer.Ordinal))
        {
            try
            {
                parsed.Add(
                    (
                        file,
                        Parser.Parse(
                            file.Text,
                            new ParserOptions { Ignore = IgnoreOptions.Comments }
                        )
                    )
                );
            }
            catch (GraphQLParser.Exceptions.GraphQLParserException ex)
            {
                errors.Add(
                    $"{file.Source}:{ex.Location.Line}:{ex.Location.Column}: not GraphQL: {ex.Description}."
                );
            }
        }

        var fragments = new Dictionary<
            string,
            (string Source, GraphQLFragmentDefinition Definition)
        >(StringComparer.Ordinal);
        foreach (var (file, document) in parsed)
        foreach (var fragment in document.Definitions.OfType<GraphQLFragmentDefinition>())
        {
            var name = fragment.FragmentName.Name.StringValue;
            if (fragments.ContainsKey(name))
                errors.Add(At(file, fragment, $"the fragment '{name}' is defined twice."));
            else
                fragments[name] = (file.Source, fragment);
        }

        var checker = new Checker(types, fragments, errors);
        foreach (var (name, (source, fragment)) in fragments)
        {
            var file = parsed.First(p => p.File.Source == source).File;
            var on = fragment.TypeCondition.Type.Name.StringValue;
            if (
                types.Definition(on)
                is not (
                    GraphQLObjectTypeDefinition
                    or GraphQLInterfaceTypeDefinition
                    or GraphQLUnionTypeDefinition
                )
            )
                errors.Add(
                    At(
                        file,
                        fragment,
                        $"the fragment '{name}' is on '{on}', which the manifest's types do not hold as an object, interface or union."
                    )
                );
            if (HasDirective(fragment))
                errors.Add(
                    At(
                        file,
                        fragment,
                        $"the fragment '{name}' uses a directive; a client document uses none."
                    )
                );
        }

        var operations = new List<ClientOperation>();
        foreach (var (file, document) in parsed)
        {
            var ops = document.Definitions.OfType<GraphQLOperationDefinition>().ToList();
            if (
                document.Definitions.Any(d =>
                    d is not (GraphQLOperationDefinition or GraphQLFragmentDefinition)
                )
            )
            {
                errors.Add(
                    $"{file.Source}: holds a definition that is neither an operation nor a fragment."
                );
                continue;
            }
            if (ops.Count == 0)
                continue;
            if (ops.Count > 1)
            {
                errors.Add(At(file, ops[1], "holds a second operation; a file holds one."));
                continue;
            }
            if (Operation(file, ops[0], manifest, types, checker) is { } operation)
                operations.Add(operation);
        }

        foreach (
            var group in operations
                .GroupBy(o => o.Name, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
        )
            errors.Add(
                $"{string.Join(" and ", group.Select(o => o.Source))}: the operation name '{group.Key}' is used twice; operation names are unique."
            );
        foreach (var (name, (source, fragment)) in fragments)
            if (!checker.Used.Contains(name))
                errors.Add(
                    At(
                        parsed.First(p => p.File.Source == source).File,
                        fragment,
                        $"the fragment '{name}' is spread by no operation."
                    )
                );

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "The client documents are not ones the host can register:\n  "
                    + string.Join("\n  ", errors)
            );

        // Each document is self-contained: its operation, then every fragment it spreads, in name order.
        var texts = operations
            .Select(o =>
            {
                var used = checker.FragmentsOf(o.Operation);
                var text =
                    Print(o.Operation)
                    + string.Concat(
                        used.Order(StringComparer.Ordinal)
                            .Select(f => "\n\n" + Print(fragments[f].Definition))
                    )
                    + "\n";
                return o with { Text = text, Id = IdOf(o.Name, text) };
            })
            .OrderBy(o => o.Name, StringComparer.Ordinal)
            .ToList();
        return new ClientDocumentSet(texts, fragments);
    }

    private static ClientOperation? Operation(
        ClientDocumentFile file,
        GraphQLOperationDefinition operation,
        ClientManifest manifest,
        SdlTypes types,
        Checker checker
    )
    {
        var errors = checker.Errors;
        var before = errors.Count;
        if (operation.Name is not { } nameNode)
        {
            errors.Add(
                At(
                    file,
                    operation,
                    "the operation has no name; name it, and the generated function is named after it."
                )
            );
            return null;
        }
        var name = nameNode.StringValue;
        if (operation.Operation is not (OperationType.Query or OperationType.Mutation))
        {
            errors.Add(
                At(
                    file,
                    operation,
                    $"'{name}' is a {operation.Operation.ToString().ToLowerInvariant()}; a client document is a query or a mutation."
                )
            );
            return null;
        }
        if (HasDirective(operation))
        {
            errors.Add(
                At(
                    file,
                    operation,
                    $"'{name}' uses a directive; a client document uses none (@include and @skip are not supported)."
                )
            );
            return null;
        }

        var mutation = operation.Operation == OperationType.Mutation;
        var root = mutation ? "dispatch" : "discover";
        var rootKind = mutation ? "mutation" : "query";
        if (
            operation.SelectionSet.Selections
                is not [GraphQLField { Alias: null, Arguments: null } top]
            || top.Name.StringValue != root
        )
        {
            errors.Add(
                At(file, operation, $"'{name}' selects something other than '{root}' at its root.")
            );
            return null;
        }

        var variables = (operation.Variables?.Items ?? []).ToDictionary(
            v => v.Variable.Name.StringValue,
            v => v,
            StringComparer.Ordinal
        );
        foreach (var (variable, definition) in variables)
            if (
                types.NameOf(definition.Type) is { } typeName
                && HoldsFilterInput(types, manifest.FilterInputs, typeName, [])
            )
                errors.Add(
                    At(
                        file,
                        definition,
                        $"'{name}' declares '${variable}' as {typeName}, which is or holds a filter or sort input; write "
                            + "the filter in the document and pass only its values as variables."
                    )
                );
        var scope = new Scope(file, variables);
        checker.Begin(operation);
        var trains = new List<ClientTrainUse>();
        var trainsOf = Reachable(manifest, rootKind);
        foreach (var selection in top.SelectionSet?.Selections ?? [])
        {
            if (selection is not GraphQLField field)
            {
                errors.Add(
                    At(
                        file,
                        selection,
                        $"'{name}' spreads a fragment directly under '{root}'; select the trains themselves."
                    )
                );
                continue;
            }
            var fieldName = field.Name.StringValue;
            if (
                trainsOf.FirstOrDefault(t =>
                    t.Train.Namespace is null && t.Train.Field == fieldName
                )
                    is var (train, queryModel)
                && train is not null
            )
            {
                trains.Add(checker.Train(scope, train, field, [root, Key(field)], queryModel));
                continue;
            }
            var inNamespace = trainsOf.Where(t => t.Train.Namespace == fieldName).ToList();
            if (inNamespace.Count == 0)
            {
                errors.Add(
                    At(
                        file,
                        field,
                        mutation && IsQueryModel(manifest, null, fieldName)
                            ? $"'{root}.{fieldName}' is a query model; a mutation cannot hold one."
                            : $"'{root}.{fieldName}' is no train, query model or namespace the host exposes."
                    )
                );
                continue;
            }
            if (field.Alias is not null || field.Arguments is not null)
            {
                errors.Add(
                    At(
                        file,
                        field,
                        $"the namespace '{fieldName}' takes no alias and no argument; alias a train."
                    )
                );
                continue;
            }
            foreach (var inner in field.SelectionSet?.Selections ?? [])
            {
                if (
                    inner is not GraphQLField trainField
                    || inNamespace.FirstOrDefault(t => t.Train.Field == trainField.Name.StringValue)
                        is not
                        ({ } nsTrain, var nsQueryModel)
                )
                {
                    errors.Add(
                        At(
                            file,
                            inner,
                            $"selects something other than a train or query model under the namespace '{fieldName}'."
                        )
                    );
                    continue;
                }
                trains.Add(
                    checker.Train(
                        scope,
                        nsTrain,
                        trainField,
                        [root, fieldName, Key(trainField)],
                        nsQueryModel
                    )
                );
            }
        }
        if (trains.Count == 0 && errors.Count == before)
            errors.Add(At(file, operation, $"'{name}' selects no train or query model."));
        if (mutation && trains.Count(t => !t.QueryModel) > 1)
            errors.Add(
                At(
                    file,
                    operation,
                    $"'{name}' runs {trains.Count} trains in one mutation; a mutation runs one."
                )
            );
        foreach (var (variable, definition) in variables)
            if (!scope.UsedVariables.Contains(variable))
                errors.Add(
                    At(
                        file,
                        definition,
                        $"'{name}' declares the variable '${variable}' and never uses it."
                    )
                );

        return errors.Count == before
            ? new ClientOperation(name, file.Source, operation, root, trains, "", "")
            : null;
    }

    /// <summary>
    /// What a document under <paramref name="rootKind"/> may select: the manifest's trains under that root and, in a
    /// query, its query models, each written as a train's entry.
    /// </summary>
    internal static IReadOnlyList<(ClientManifestTrain Train, bool QueryModel)> Reachable(
        ClientManifest manifest,
        string rootKind
    ) =>
        [
            .. manifest.Trains.Where(t => t.Root == rootKind).Select(t => (t, false)),
            .. rootKind == "query"
                ? manifest.QueryModels.Select(q =>
                    (
                        new ClientManifestTrain
                        {
                            Field = q.Field,
                            Root = "query",
                            Namespace = q.Namespace,
                            Answer = q.Answer,
                            Arguments = q.Arguments,
                            Requires = q.Requires,
                        },
                        true
                    )
                )
                : [],
        ];

    private static bool IsQueryModel(ClientManifest manifest, string? ns, string field) =>
        manifest.QueryModels.Any(q => q.Namespace == ns && q.Field == field);

    // Whether an input type is, or holds at any depth, one the manifest names as a filter or sort input.
    private static bool HoldsFilterInput(
        SdlTypes types,
        IReadOnlyList<string> filterInputs,
        string name,
        HashSet<string> seen
    )
    {
        if (filterInputs.Contains(name, StringComparer.Ordinal))
            return true;
        if (!seen.Add(name) || types.Definition(name) is not GraphQLInputObjectTypeDefinition input)
            return false;
        return (input.Fields?.Items ?? []).Any(f =>
            types.NameOf(f.Type) is { } inner && HoldsFilterInput(types, filterInputs, inner, seen)
        );
    }

    private static string Key(GraphQLField field) =>
        field.Alias?.Name.StringValue ?? field.Name.StringValue;

    private static string Print(ASTNode node)
    {
        var writer = new StringWriter();
        new SDLPrinter(new SDLPrinterOptions { PrintComments = false })
            .PrintAsync(node, writer)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return writer.ToString().Replace("\r\n", "\n").Trim();
    }

    internal static string At(ClientDocumentFile file, ASTNode node, string message)
    {
        var (line, column) = Position(file.Text, node.Location.Start);
        return $"{file.Source}:{line}:{column}: {message}";
    }

    private static (int Line, int Column) Position(string text, int offset)
    {
        var line = 1;
        var column = 1;
        for (var i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
                column++;
        }
        return (line, column);
    }

    private static bool HasDirective(ASTNode node) =>
        node switch
        {
            IHasDirectivesNode { Directives.Items.Count: > 0 } => true,
            _ => Children(node).Any(HasDirective),
        };

    private static IEnumerable<ASTNode> Children(ASTNode node) =>
        node switch
        {
            GraphQLOperationDefinition o => (o.Variables?.Items ?? [])
                .Cast<ASTNode>()
                .Append(o.SelectionSet),
            GraphQLFragmentDefinition f => [f.SelectionSet],
            GraphQLSelectionSet s => s.Selections,
            GraphQLField f => f.SelectionSet is { } set ? [set] : [],
            GraphQLInlineFragment i => [i.SelectionSet],
            _ => [],
        };

    // The variables an operation declares, and which of them its fields use.
    private sealed class Scope(
        ClientDocumentFile file,
        IReadOnlyDictionary<string, GraphQLVariableDefinition> variables
    )
    {
        public ClientDocumentFile File { get; } = file;
        public IReadOnlyDictionary<string, GraphQLVariableDefinition> Variables { get; } =
            variables;
        public HashSet<string> UsedVariables { get; } = new(StringComparer.Ordinal);
    }

    // Checks selections against the manifest's types, and remembers which fragments each operation spreads.
    private sealed partial class Checker(
        SdlTypes types,
        IReadOnlyDictionary<
            string,
            (string Source, GraphQLFragmentDefinition Definition)
        > fragments,
        List<string> errors
    )
    {
        private readonly Dictionary<GraphQLOperationDefinition, HashSet<string>> _spreads = [];
        private readonly SdlTypes _types = types;
        private readonly IReadOnlyDictionary<
            string,
            (string Source, GraphQLFragmentDefinition Definition)
        > _fragments = fragments;

        public List<string> Errors { get; } = errors;

        public HashSet<string> Used { get; } = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> FragmentsOf(GraphQLOperationDefinition operation) =>
            _spreads.TryGetValue(operation, out var spread) ? spread : [];

        private GraphQLOperationDefinition? _current;

        public void Begin(GraphQLOperationDefinition operation) => _current = operation;

        public ClientTrainUse Train(
            Scope scope,
            ClientManifestTrain train,
            GraphQLField field,
            IReadOnlyList<string> path,
            bool queryModel = false
        )
        {
            var kind = queryModel ? "query model" : "train";
            var arguments = TrainArguments(train);
            foreach (var argument in field.Arguments?.Items ?? [])
            {
                var argName = argument.Name.StringValue;
                if (!arguments.TryGetValue(argName, out var declared))
                {
                    Errors.Add(
                        At(
                            scope.File,
                            argument,
                            $"the {kind} '{train.Field}' takes no argument '{argName}'."
                        )
                    );
                    continue;
                }
                Value(
                    scope,
                    argument.Value,
                    declared.Type,
                    $"the argument '{argName}' of '{train.Field}'"
                );
            }
            foreach (var (argName, (argType, hasDefault)) in arguments)
                if (
                    argType is GraphQLNonNullType
                    && !hasDefault
                    && field.Arguments?.Items.Any(a => a.Name.StringValue == argName) != true
                )
                    Errors.Add(
                        At(
                            scope.File,
                            field,
                            $"the {kind} '{train.Field}' requires the argument '{argName}'."
                        )
                    );

            var answer = SdlTypes.ParseType(train.Answer, $"the {kind} '{train.Field}'");
            if (answer is null)
                throw new InvalidOperationException(
                    $"The manifest does not say what the {kind} '{train.Field}' answers with. Export the manifest "
                        + "again from the host."
                );
            Selection(scope, field, answer, train.Field, []);
            return new ClientTrainUse(train, field, path, queryModel);
        }
    }

    /// <summary>
    /// The arguments of a train's field, as the manifest lists them: each one's type, and whether it has a default, so a
    /// document may leave out a non-null argument that has one.
    /// </summary>
    internal static Dictionary<string, (GraphQLType Type, bool HasDefault)> TrainArguments(
        ClientManifestTrain train
    )
    {
        var result = new Dictionary<string, (GraphQLType, bool)>(StringComparer.Ordinal);
        foreach (var argument in train.Arguments)
            if (
                SdlTypes.ParseType(
                    argument.Type,
                    $"the argument '{argument.Name}' of '{train.Field}'"
                ) is
                { } type
            )
                result[argument.Name] = (type, argument.Default is not null);
        return result;
    }

    private sealed partial class Checker
    {
        private void Selection(
            Scope scope,
            GraphQLField field,
            GraphQLType type,
            string where,
            List<string> fragmentStack
        )
        {
            var named = Named(type);
            var definition = _types.Definition(named);
            var composite =
                definition
                is GraphQLObjectTypeDefinition
                    or GraphQLInterfaceTypeDefinition
                    or GraphQLUnionTypeDefinition;
            if (composite && field.SelectionSet is null)
            {
                Errors.Add(
                    At(scope.File, field, $"'{where}' is an object ({named}); select its fields.")
                );
                return;
            }
            if (!composite && field.SelectionSet is not null)
            {
                Errors.Add(
                    At(scope.File, field, $"'{where}' is a {named}, which has no fields to select.")
                );
                return;
            }
            if (composite)
                Set(scope, field.SelectionSet!, named, fragmentStack);
        }

        private void Set(
            Scope scope,
            GraphQLSelectionSet set,
            string typeName,
            List<string> fragmentStack
        )
        {
            var keys = new Dictionary<string, (string Field, string Arguments)>(
                StringComparer.Ordinal
            );
            foreach (var selection in set.Selections)
                switch (selection)
                {
                    case GraphQLField field:
                        Field(scope, field, typeName, fragmentStack, keys);
                        break;
                    case GraphQLInlineFragment inline:
                        var on = inline.TypeCondition?.Type.Name.StringValue ?? typeName;
                        if (!Overlaps(on, typeName))
                        {
                            Errors.Add(
                                At(
                                    scope.File,
                                    inline,
                                    $"'... on {on}' can never apply to a {typeName}."
                                )
                            );
                            break;
                        }
                        Set(scope, inline.SelectionSet, on, fragmentStack);
                        break;
                    case GraphQLFragmentSpread spread:
                        var name = spread.FragmentName.Name.StringValue;
                        if (!_fragments.TryGetValue(name, out var fragment))
                        {
                            Errors.Add(
                                At(scope.File, spread, $"no file defines the fragment '{name}'.")
                            );
                            break;
                        }
                        if (fragmentStack.Contains(name))
                        {
                            Errors.Add(
                                At(
                                    scope.File,
                                    spread,
                                    $"the fragment '{name}' spreads itself ({string.Join(" > ", fragmentStack.Append(name))})."
                                )
                            );
                            break;
                        }
                        Used.Add(name);
                        if (_current is { } op)
                        {
                            if (!_spreads.TryGetValue(op, out var spreads))
                                _spreads[op] = spreads = new(StringComparer.Ordinal);
                            spreads.Add(name);
                        }
                        var condition = fragment.Definition.TypeCondition.Type.Name.StringValue;
                        if (!Overlaps(condition, typeName))
                        {
                            Errors.Add(
                                At(
                                    scope.File,
                                    spread,
                                    $"the fragment '{name}' is on {condition}, which can never apply to a {typeName}."
                                )
                            );
                            break;
                        }
                        Set(
                            scope,
                            fragment.Definition.SelectionSet,
                            condition,
                            [.. fragmentStack, name]
                        );
                        break;
                }
        }

        private void Field(
            Scope scope,
            GraphQLField field,
            string typeName,
            List<string> fragmentStack,
            Dictionary<string, (string Field, string Arguments)> keys
        )
        {
            var name = field.Name.StringValue;
            var key = Key(field);
            var printedArguments = string.Join(
                ",",
                (field.Arguments?.Items ?? []).Select(a =>
                    a.Name.StringValue + ":" + Print(a.Value)
                )
            );
            if (
                keys.TryGetValue(key, out var seen)
                && (seen.Field != name || seen.Arguments != printedArguments)
            )
                Errors.Add(
                    At(
                        scope.File,
                        field,
                        $"'{key}' names two different selections under {typeName}; alias one of them."
                    )
                );
            keys[key] = (name, printedArguments);

            if (name == "__typename")
            {
                if (field.Arguments is not null || field.SelectionSet is not null)
                    Errors.Add(
                        At(scope.File, field, "__typename takes no argument and no selection.")
                    );
                return;
            }
            var definition = _types.Definition(typeName) switch
            {
                GraphQLObjectTypeDefinition o => o.Fields?.Items.FirstOrDefault(f =>
                    f.Name.StringValue == name
                ),
                GraphQLInterfaceTypeDefinition i => i.Fields?.Items.FirstOrDefault(f =>
                    f.Name.StringValue == name
                ),
                _ => null,
            };
            if (definition is null)
            {
                Errors.Add(At(scope.File, field, $"{typeName} has no field '{name}'."));
                return;
            }
            var declared = (definition.Arguments?.Items ?? []).ToDictionary(
                a => a.Name.StringValue,
                a => a,
                StringComparer.Ordinal
            );
            foreach (var argument in field.Arguments?.Items ?? [])
            {
                if (!declared.TryGetValue(argument.Name.StringValue, out var argDefinition))
                {
                    Errors.Add(
                        At(
                            scope.File,
                            argument,
                            $"{typeName}.{name} takes no argument '{argument.Name.StringValue}'."
                        )
                    );
                    continue;
                }
                Value(
                    scope,
                    argument.Value,
                    argDefinition.Type,
                    $"the argument '{argument.Name.StringValue}' of {typeName}.{name}"
                );
            }
            foreach (var (argName, argDefinition) in declared)
                if (
                    argDefinition.Type is GraphQLNonNullType
                    && argDefinition.DefaultValue is null
                    && field.Arguments?.Items.Any(a => a.Name.StringValue == argName) != true
                )
                    Errors.Add(
                        At(
                            scope.File,
                            field,
                            $"{typeName}.{name} requires the argument '{argName}'."
                        )
                    );
            Selection(scope, field, definition.Type, $"{typeName}.{name}", fragmentStack);
        }

        // A value given to an argument: each variable it holds is declared, used, and of a type the argument takes.
        private void Value(Scope scope, GraphQLValue value, GraphQLType expected, string where)
        {
            switch (value)
            {
                case GraphQLVariable variable:
                    var name = variable.Name.StringValue;
                    if (!scope.Variables.TryGetValue(name, out var definition))
                    {
                        Errors.Add(
                            At(
                                scope.File,
                                variable,
                                $"'${name}' is used by {where} but the operation does not declare it."
                            )
                        );
                        return;
                    }
                    scope.UsedVariables.Add(name);
                    if (!Accepts(expected, definition.Type, definition.DefaultValue is not null))
                        Errors.Add(
                            At(
                                scope.File,
                                variable,
                                $"'${name}' is {Print(definition.Type)}, but {where} takes {Print(expected)}."
                            )
                        );
                    break;
                case GraphQLListValue list:
                    foreach (var item in list.Values ?? [])
                        Value(
                            scope,
                            item,
                            expected is GraphQLNonNullType nn
                                    ? (nn.Type is GraphQLListType l ? l.Type : nn.Type)
                                : expected is GraphQLListType l2 ? l2.Type
                                : expected,
                            where
                        );
                    break;
                case GraphQLObjectValue obj:
                    if (
                        _types.Definition(Named(expected))
                        is not GraphQLInputObjectTypeDefinition input
                    )
                    {
                        if (Named(expected) != "Unknown")
                            Errors.Add(
                                At(
                                    scope.File,
                                    obj,
                                    $"{where} takes {Print(expected)}, not an object."
                                )
                            );
                        foreach (var field in obj.Fields ?? [])
                            Value(
                                scope,
                                field.Value,
                                new GraphQLNamedType(new GraphQLName("Unknown")),
                                where
                            );
                        break;
                    }
                    var declared = (input.Fields?.Items ?? []).ToDictionary(
                        f => f.Name.StringValue,
                        StringComparer.Ordinal
                    );
                    foreach (var field in obj.Fields ?? [])
                    {
                        var fieldName = field.Name.StringValue;
                        if (!declared.TryGetValue(fieldName, out var inputField))
                        {
                            Errors.Add(
                                At(
                                    scope.File,
                                    field,
                                    $"{where} has no field '{fieldName}' in {input.Name.StringValue}."
                                )
                            );
                            continue;
                        }
                        Value(
                            scope,
                            field.Value,
                            inputField.Type,
                            $"'{fieldName}' of {input.Name.StringValue}"
                        );
                    }
                    foreach (var (fieldName, inputField) in declared)
                        if (
                            inputField.Type is GraphQLNonNullType
                            && inputField.DefaultValue is null
                            && obj.Fields?.Any(f => f.Name.StringValue == fieldName) != true
                        )
                            Errors.Add(
                                At(
                                    scope.File,
                                    obj,
                                    $"{where} requires '{fieldName}' of {input.Name.StringValue}."
                                )
                            );
                    break;
                case GraphQLNullValue:
                    if (expected is GraphQLNonNullType)
                        Errors.Add(
                            At(scope.File, value, $"{where} takes {Print(expected)}, not null.")
                        );
                    break;
                default:
                    if (Literal(value, expected) is { } refusal)
                        Errors.Add(At(scope.File, value, $"{where} {refusal}"));
                    break;
            }
        }

        // Why a scalar or enum literal is not a value of `expected`, or null when it is one or the type is not one the
        // check knows (a custom scalar takes what its parser takes, which only the host knows).
        private string? Literal(GraphQLValue value, GraphQLType expected)
        {
            var name = Named(expected);
            if (_types.Definition(name) is GraphQLEnumTypeDefinition @enum)
                return
                    value is GraphQLEnumValue e
                    && (@enum.Values?.Items ?? []).Any(v =>
                        v.Name.StringValue == e.Name.StringValue
                    )
                    ? null
                    : $"takes one of {name}'s values, not {Print(value)}.";
            var fits = name switch
            {
                "Int" => value is GraphQLIntValue,
                "Float" => value is GraphQLIntValue or GraphQLFloatValue,
                "String" => value is GraphQLStringValue,
                "ID" => value is GraphQLStringValue or GraphQLIntValue,
                "Boolean" => value is GraphQLBooleanValue,
                _ => true,
            };
            if (expected is GraphQLNonNullType { Type: GraphQLListType } or GraphQLListType)
                fits = true;
            return fits ? null : $"takes {Print(expected)}, not {Print(value)}.";
        }

        // Whether a variable of `given` (with or without a default) may be passed where `expected` is taken.
        private static bool Accepts(GraphQLType expected, GraphQLType given, bool hasDefault)
        {
            if (Named(expected) == "Unknown")
                return true;
            if (expected is GraphQLNonNullType expectedInner)
                return given is GraphQLNonNullType givenInner
                    ? Accepts(expectedInner.Type, givenInner.Type, false)
                    : hasDefault && Accepts(expectedInner.Type, given, false);
            if (given is GraphQLNonNullType nonNull)
                return Accepts(expected, nonNull.Type, false);
            return (expected, given) switch
            {
                (GraphQLListType e, GraphQLListType g) => Accepts(e.Type, g.Type, false),
                (GraphQLNamedType e, GraphQLNamedType g) => e.Name.StringValue
                    == g.Name.StringValue,
                _ => false,
            };
        }

        // Whether a value of the type `a` may be one of `b`: the same type, or two that share a possible object type.
        private bool Overlaps(string a, string b)
        {
            if (a == b)
                return true;
            var pa = Possible(a);
            var pb = Possible(b);
            return pa.Overlaps(pb);
        }

        private HashSet<string> Possible(string name) =>
            _types.Definition(name) is GraphQLObjectTypeDefinition
                ? [name]
                : _types.PossibleTypes(name).ToHashSet(StringComparer.Ordinal);

        private static string Named(GraphQLType type) =>
            type switch
            {
                GraphQLNonNullType n => Named(n.Type),
                GraphQLListType l => Named(l.Type),
                GraphQLNamedType named => named.Name.StringValue,
                _ => "",
            };
    }
}
