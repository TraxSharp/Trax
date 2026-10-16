using System.Security.Cryptography;
using System.Text;
using GraphQLParser;
using GraphQLParser.AST;
using GraphQLParser.Visitors;
using Trax.Effect.StateMachine;

namespace Trax.Cli.Clients;

/// <summary>One <c>.graphql</c> file a frontend wrote, by the path it is named under and its text.</summary>
internal sealed record ClientDocumentFile(string Source, string Text);

/// <summary>A train a client document selects: its manifest entry, its field, and the path to its value.</summary>
internal sealed record ClientTrainUse(
    ClientManifestTrain Train,
    GraphQLField Field,
    IReadOnlyList<string> Path
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
/// under it only the manifest's trains and their namespaces, a mutation exactly one train, no directive; every field,
/// argument, variable and fragment as the manifest's types allow. A file of fragments only holds named fragments any
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
        var scope = new Scope(file, variables);
        checker.Begin(operation);
        var trains = new List<ClientTrainUse>();
        var trainsOf = manifest.Trains.Where(t => t.Root == rootKind).ToList();
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
                trainsOf.FirstOrDefault(t => t.Namespace is null && t.Field == fieldName) is
                { } train
            )
            {
                trains.Add(checker.Train(scope, train, field, [root, Key(field)]));
                continue;
            }
            var inNamespace = trainsOf.Where(t => t.Namespace == fieldName).ToList();
            if (inNamespace.Count == 0)
            {
                errors.Add(
                    At(
                        file,
                        field,
                        $"'{root}.{fieldName}' is no train or namespace the host exposes."
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
                    || inNamespace.FirstOrDefault(t => t.Field == trainField.Name.StringValue)
                        is not { } nsTrain
                )
                {
                    errors.Add(
                        At(
                            file,
                            inner,
                            $"selects something other than a train under the namespace '{fieldName}'."
                        )
                    );
                    continue;
                }
                trains.Add(
                    checker.Train(scope, nsTrain, trainField, [root, fieldName, Key(trainField)])
                );
            }
        }
        if (trains.Count == 0 && errors.Count == before)
            errors.Add(At(file, operation, $"'{name}' selects no train."));
        if (mutation && trains.Count > 1)
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
            IReadOnlyList<string> path
        )
        {
            var arguments = TrainArguments(train);
            foreach (var argument in field.Arguments?.Items ?? [])
            {
                var argName = argument.Name.StringValue;
                if (!arguments.TryGetValue(argName, out var argType))
                {
                    Errors.Add(
                        At(
                            scope.File,
                            argument,
                            $"the train '{train.Field}' takes no argument '{argName}'."
                        )
                    );
                    continue;
                }
                Value(
                    scope,
                    argument.Value,
                    argType,
                    $"the argument '{argName}' of '{train.Field}'"
                );
            }
            foreach (var (argName, argType) in arguments)
                if (
                    argType is GraphQLNonNullType
                    && field.Arguments?.Items.Any(a => a.Name.StringValue == argName) != true
                )
                    Errors.Add(
                        At(
                            scope.File,
                            field,
                            $"the train '{train.Field}' requires the argument '{argName}'."
                        )
                    );

            var answer = SdlTypes.ParseType(train.Answer, $"the train '{train.Field}'");
            if (answer is not null)
                Selection(scope, field, answer, train.Field, []);
            return new ClientTrainUse(train, field, path);
        }
    }

    /// <summary>The arguments of a train's field, as its generated document declares them: each variable's type.</summary>
    internal static Dictionary<string, GraphQLType> TrainArguments(ClientManifestTrain train)
    {
        var result = new Dictionary<string, GraphQLType>(StringComparer.Ordinal);
        if (train.Documents.Count == 0)
            return result;
        var document = Parser.Parse(train.Documents[0].Document);
        var operation = document.Definitions.OfType<GraphQLOperationDefinition>().First();
        var variables = (operation.Variables?.Items ?? []).ToDictionary(
            v => v.Variable.Name.StringValue,
            v => v.Type
        );
        var set = operation.SelectionSet;
        GraphQLField? trainField = null;
        while (set?.Selections.OfType<GraphQLField>().FirstOrDefault() is { } f)
        {
            if (f.Name.StringValue == train.Field)
            {
                trainField = f;
                break;
            }
            set = f.SelectionSet;
        }
        foreach (var argument in trainField?.Arguments?.Items ?? [])
            if (
                argument.Value is GraphQLVariable v
                && variables.TryGetValue(v.Name.StringValue, out var type)
            )
                result[argument.Name.StringValue] = type;
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
                    foreach (var field in obj.Fields ?? [])
                        Value(
                            scope,
                            field.Value,
                            new GraphQLNamedType(new GraphQLName("Unknown")),
                            where
                        );
                    break;
            }
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
