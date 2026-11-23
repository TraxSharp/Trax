using System.Security.Cryptography;
using System.Text;
using GraphQLParser.AST;

namespace Trax.Cli.Clients;

internal sealed partial class ClientDocumentSet
{
    /// <summary>The id prefix a host stores a console document under.</summary>
    public const string ConsolePrefix = "trax_console_";

    /// <summary>The root field every query and mutation of the console selects.</summary>
    public const string OperationsField = "operations";

    /// <summary>The root field of persisted-operation management, which a host runs as text and never registers.</summary>
    public const string ManagementField = "persistedOperations";

    /// <summary>
    /// The subscription fields a console document may select: the operator's streams, the same set the host holds a
    /// console document to.
    /// </summary>
    public static readonly IReadOnlySet<string> OperatorStreams = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "onTrainStateChanged",
        "onDataChanged",
        "onJunctionEvent",
        "onMachineInstanceChanged",
    };

    /// <summary>
    /// The id a host stores the console document <paramref name="name"/> with <paramref name="text"/> under:
    /// <c>trax_console_&lt;name&gt;_&lt;textHash&gt;&lt;check&gt;</c>, built as a client document's is.
    /// </summary>
    public static string ConsoleIdOf(string name, string text)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var check = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(hash + "\n" + text))
        )[..16];
        return $"{ConsolePrefix}{name.Replace('_', '-')}_{hash}{check}";
    }

    /// <summary>
    /// Reads and checks the operations console's <c>.graphql</c> files against the operations schema, by the rules a
    /// host registers a console document under: one named operation per file, no directive, no alias at the root; a
    /// query or mutation selects only <c>operations</c>, or only <c>persistedOperations</c> (which has no id, since a
    /// host runs it as text behind its own gate); a subscription selects exactly one of the operator's streams. Every
    /// field, argument, variable and fragment is checked against the schema. Every refusal is reported at once.
    /// </summary>
    /// <exception cref="InvalidOperationException">A file breaks a rule.</exception>
    public static ClientDocumentSet ReadOperations(
        IReadOnlyList<ClientDocumentFile> files,
        SdlTypes types,
        OperationsRoots roots
    ) =>
        Read(
            files,
            types,
            (file, operation, checker) =>
                OperationsOperation(file, operation, types, roots, checker),
            (operation, text) =>
                string.Equals(operation.Root, ManagementField, StringComparison.Ordinal)
                    ? ""
                    : ConsoleIdOf(operation.Name, text)
        );

    private static ClientOperation? OperationsOperation(
        ClientDocumentFile file,
        GraphQLOperationDefinition operation,
        SdlTypes types,
        OperationsRoots roots,
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
        if (HasDirective(operation))
        {
            errors.Add(
                At(file, operation, $"'{name}' uses a directive; a console document uses none.")
            );
            return null;
        }

        if (RootFields(file, operation, name, errors) is not { } fields)
            return null;
        if (
            OperationsRootOf(file, operation, name, fields, roots, errors)
            is not
            (string root, string rootType)
        )
            return null;
        if (types.Definition(rootType) is not GraphQLObjectTypeDefinition)
        {
            errors.Add(
                At(
                    file,
                    operation,
                    $"'{name}' is a {operation.Operation.ToString().ToLowerInvariant()}, and the operations schema has no {rootType}."
                )
            );
            return null;
        }

        if (Variables(file, name, operation, errors) is not { } variables)
            return null;
        var scope = new Scope(file, variables);
        checker.Begin(operation);
        checker.Root(scope, operation.SelectionSet, rootType);
        RefuseUnusedVariables(file, name, variables, scope, errors);

        return errors.Count == before
            ? new ClientOperation(name, file.Source, operation, root, [], "", "")
            : null;
    }

    // The fields at the operation's root, or null (with the error) when it selects anything else or
    // an alias there.
    private static List<GraphQLField>? RootFields(
        ClientDocumentFile file,
        GraphQLOperationDefinition operation,
        string name,
        List<string> errors
    )
    {
        var fields = new List<GraphQLField>();
        foreach (var selection in operation.SelectionSet.Selections)
        {
            if (selection is not GraphQLField { Alias: null } field)
            {
                errors.Add(
                    At(
                        file,
                        selection,
                        $"'{name}' selects something other than a field, or an alias, at its root."
                    )
                );
                return null;
            }
            fields.Add(field);
        }
        return fields;
    }

    // The root field and its type the operation reaches, or null (with the error) when it reaches
    // none the operations schema serves.
    private static (string Root, string RootType)? OperationsRootOf(
        ClientDocumentFile file,
        GraphQLOperationDefinition operation,
        string name,
        List<GraphQLField> fields,
        OperationsRoots roots,
        List<string> errors
    )
    {
        if (operation.Operation == OperationType.Subscription)
        {
            if (fields is not [var stream] || !OperatorStreams.Contains(stream.Name.StringValue))
            {
                errors.Add(
                    At(
                        file,
                        operation,
                        $"'{name}' subscribes to something other than one of the operator's streams ({string.Join(", ", OperatorStreams.Order(StringComparer.Ordinal))})."
                    )
                );
                return null;
            }
            return (stream.Name.StringValue, roots.Subscription);
        }
        var names = fields
            .Select(f => f.Name.StringValue)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (names is not [var only] || only is not (OperationsField or ManagementField))
        {
            errors.Add(
                At(
                    file,
                    operation,
                    $"'{name}' selects something other than '{OperationsField}', or '{ManagementField}' alone, at its root."
                )
            );
            return null;
        }
        return (only, operation.Operation == OperationType.Mutation ? roots.Mutation : roots.Query);
    }

    private sealed partial class Checker
    {
        /// <summary>Checks an operation's selection against the root type it is answered from.</summary>
        public void Root(Scope scope, GraphQLSelectionSet set, string rootType) =>
            Set(scope, set, rootType, []);
    }
}
