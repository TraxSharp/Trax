using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HotChocolate;
using HotChocolate.Types;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// Generates the persisted documents of each exposed machine's operations from the built schema, one per
/// operation, each under an id in the reserved prefix that carries the machine's IR hash and a hash of its text
/// together with that IR hash.
/// </summary>
/// <remarks>
/// <para>
/// The id is <c>trax_machine_&lt;machine&gt;_&lt;operation&gt;_&lt;irHash&gt;&lt;textHash&gt;</c>: the machine's id,
/// the operation's field name, the 64 hex characters of the machine's IR hash (<see cref="IMachine.SchemaHash"/>),
/// then the first 16 hex characters of the SHA-256 of the IR hash and the document's text. HotChocolate refuses a dot
/// in a persisted id, and a machine id is kebab-case and an operation camelCase, so <c>_</c> separates them: the machine
/// is the segment up to the first <c>_</c> after the prefix (a machine id never holds one, and an operation may), and the
/// hash the segment after the last. A change
/// to the machine changes every id, and a change to the text alone (a GraphQL naming change) changes that
/// document's id, so an id names one document for one machine for as long as it exists.
/// </para>
/// <para>
/// A document takes its values as variables, one per argument of its field, named like the argument: <c>input</c>
/// and <c>requestId</c> for <c>start</c>; <c>id</c>, <c>input</c> (when the event has one), <c>requestId</c> and
/// <c>clientResult</c> for an event; <c>id</c> and <c>requestId</c> for <c>discard</c>; <c>id</c> for <c>get</c>; and
/// <c>id</c> for the push (<c>&lt;machine&gt;Changed</c>), the instance's push, whose document names its machine.
/// It selects every field of the answer, at every depth, so a client reads the whole instance. The text is built
/// from the schema in the order the schema declares fields, so two processes building one schema write the same
/// bytes.
/// </para>
/// <para>See <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </remarks>
internal static class MachineDocuments
{
    /// <summary>The id prefix every machine document is stored under, and no uploaded document may use.</summary>
    public const string ReservedIdPrefix = "trax_machine_";

    /// <summary>The operation a machine's push document is registered under.</summary>
    public const string ChangedOperation = "changed";

    /// <summary>How many hex characters of the IR hash an id carries: all of them.</summary>
    internal const int IrHashLength = 64;

    /// <summary>How many hex characters of the text hash an id carries.</summary>
    internal const int TextHashLength = 16;

    /// <summary>Whether <paramref name="id"/> is under the reserved prefix.</summary>
    public static bool IsReserved(string? id) =>
        id is not null && id.StartsWith(ReservedIdPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The documents for every operation of <paramref name="machines"/>, ordered by machine id and then as the schema
    /// declares the operations, whatever order the machines come in.
    /// </summary>
    /// <param name="schema">The built schema the documents are written against.</param>
    /// <param name="machines">The machines to generate documents for.</param>
    /// <exception cref="InvalidOperationException">
    /// A machine is not served under the schema's <c>machines</c> root, has no IR hash, or answers with a type that
    /// holds itself, which no finite document can select.
    /// </exception>
    public static IReadOnlyList<MachineDocument> Generate(
        ISchemaDefinition schema,
        IEnumerable<IMachine> machines
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(machines);

        return machines
            .DistinctBy(m => m.Name, StringComparer.Ordinal)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .SelectMany(m => ForMachine(schema, m))
            .ToList();
    }

    /// <summary>The id of a document: the prefix, the machine, the operation, the IR hash and the text's hash.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="machine"/> is empty or holds a <c>_</c>, so the id could not be read back to it.
    /// </exception>
    public static string IdOf(string machine, string operation, string irHash, string text)
    {
        // The machine segment ends at the first `_` after the prefix, and an operation may hold `_` itself, so a
        // machine id with one would read back as a shorter machine's. A machine built with the fluent Id(...) is
        // kebab-case and never has one.
        if (string.IsNullOrEmpty(machine) || machine.Contains('_'))
            throw new ArgumentException(
                $"The machine id '{machine}' cannot name persisted documents: a machine document's id separates "
                    + "the machine from the operation with '_', so the machine id must be non-empty and hold no '_'. "
                    + "Give the machine a kebab-case id, as the fluent Id(...) requires.",
                nameof(machine)
            );
        return $"{ReservedIdPrefix}{machine}_{operation}_{irHash}{TextHash(irHash, text)}";
    }

    /// <summary>
    /// The machine of a reserved id, out of <paramref name="machines"/>, or null when the id names none of them. The
    /// machine is the id's segment between the prefix and the next <c>_</c>, matched exactly: no machine id holds a
    /// <c>_</c> (<see cref="IdOf"/> refuses one), so an id of <c>order-v2</c>, or of <c>order</c> with an operation
    /// that holds <c>_</c>, is never read as another machine's.
    /// </summary>
    public static string? MachineOf(string id, IEnumerable<string> machines)
    {
        if (!IsReserved(id))
            return null;
        var end = id.IndexOf('_', ReservedIdPrefix.Length);
        if (end <= ReservedIdPrefix.Length)
            return null;
        var segment = id.AsSpan(ReservedIdPrefix.Length, end - ReservedIdPrefix.Length);
        foreach (var machine in machines)
            if (segment.SequenceEqual(machine))
                return machine;
        return null;
    }

    /// <summary>
    /// Whether the segment after the id's last <c>_</c> has the shape a generated id's hash has: an IR hash and a text
    /// hash, in lowercase hex. An id without it was never generated, and is refused without a lookup.
    /// </summary>
    public static bool IsWellFormed(string id)
    {
        var at = id.LastIndexOf('_');
        if (at < 0 || id.Length - at - 1 != IrHashLength + TextHashLength)
            return false;
        for (var i = at + 1; i < id.Length; i++)
            if (!char.IsAsciiHexDigitLower(id[i]))
                return false;
        return true;
    }

    /// <summary>The IR hash a well-formed id carries, or null for an id that is not shaped like a generated one.</summary>
    public static string? IrHashOf(string id) =>
        IsWellFormed(id) ? id[(id.LastIndexOf('_') + 1)..][..IrHashLength] : null;

    private static string TextHash(string irHash, string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(irHash + "\n" + text)))[
            ..TextHashLength
        ];

    private static IEnumerable<MachineDocument> ForMachine(
        ISchemaDefinition schema,
        IMachine machine
    )
    {
        var irHash =
            machine.SchemaHash
            ?? throw new InvalidOperationException(
                $"The machine '{machine.Name}' has no IR hash, so it has no persisted documents. Only a machine "
                    + "declared with rules can be exposed."
            );
        var field = MachineSurface.FieldName(machine.Name);
        var pascal = MachineSurface.PascalName(machine.Name);

        var documents = new List<MachineDocument>();
        foreach (
            var (keyword, root) in new[]
            {
                ("mutation", schema.MutationType),
                ("query", schema.QueryType),
            }
        )
        {
            var operations = MachineType(root, field);
            if (operations is null)
            {
                if (keyword == "query")
                    throw NotServed(machine);
                continue;
            }

            foreach (
                var operation in operations.Fields.Where(f =>
                    !f.Name.StartsWith("__", StringComparison.Ordinal)
                )
            )
            {
                var text = Document(keyword, pascal, field, operation, machine);
                documents.Add(
                    new MachineDocument(
                        IdOf(machine.Name, operation.Name, irHash, text),
                        machine.Name,
                        operation.Name,
                        text,
                        irHash
                    )
                );
            }
        }

        if (documents.Count == 0)
            throw NotServed(machine);

        // The instance's push: the machine's own field, so a client sends only the id.
        var pushField = MachineChangedSubscription.FieldOf(machine.Name);
        if (
            schema.SubscriptionType is { } subscriptions
            && subscriptions.Fields.TryGetField(pushField, out var changed)
        )
        {
            var id = changed.Arguments.First(a => a.Name == "id");
            var text =
                $"subscription {pascal}MachineChanged($id: {Print(id.Type)}) {{ "
                + $"{pushField}(id: $id)"
                + Selection(changed.Type, [], machine)
                + " }";
            documents.Add(
                new MachineDocument(
                    IdOf(machine.Name, ChangedOperation, irHash, text),
                    machine.Name,
                    ChangedOperation,
                    text,
                    irHash
                )
            );
        }
        return documents;
    }

    private static InvalidOperationException NotServed(IMachine machine) =>
        new(
            $"The machine '{machine.Name}' is not served under the machines root of this schema, so it has no "
                + "persisted documents. "
                + (
                    machine.GetType().IsDefined(typeof(TraxMachineAttribute), inherit: false)
                        ? "It is marked [TraxMachine], but AddTraxGraphQL() saw no marked machine, so the schema has "
                            + "no machines root: it reads the machines registered before it by their type or as an "
                            + "instance. Register it before AddTraxGraphQL(), for example through "
                            + "AddStateMachines(...) inside AddTrax(...), and not through a factory."
                        : "Mark it [TraxMachine]."
                )
        );

    private static IObjectTypeDefinition? MachineType(IObjectTypeDefinition? root, string field)
    {
        if (
            root is null
            || !root.Fields.TryGetField(MachineSurface.RootField, out var machines)
            || machines.Type.NamedType() is not IObjectTypeDefinition machinesType
            || !machinesType.Fields.TryGetField(field, out var machine)
        )
            return null;
        return machine.Type.NamedType() as IObjectTypeDefinition;
    }

    private static string Document(
        string keyword,
        string pascal,
        string machineField,
        IOutputFieldDefinition operation,
        IMachine machine
    )
    {
        var name = pascal + char.ToUpperInvariant(operation.Name[0]) + operation.Name[1..];
        var text = new StringBuilder();
        text.Append(keyword).Append(' ').Append(name);

        var arguments = operation.Arguments.ToList();
        if (arguments.Count > 0)
            text.Append('(')
                .AppendJoin(", ", arguments.Select(a => $"${a.Name}: {Print(a.Type)}"))
                .Append(')');

        text.Append(" { ")
            .Append(MachineSurface.RootField)
            .Append(" { ")
            .Append(machineField)
            .Append(" { ")
            .Append(operation.Name);
        if (arguments.Count > 0)
            text.Append('(')
                .AppendJoin(", ", arguments.Select(a => $"{a.Name}: ${a.Name}"))
                .Append(')');
        text.Append(Selection(operation.Type, [], machine)).Append(" } } }");
        return text.ToString();
    }

    // Every field of an object type, at every depth; nothing for a leaf. A type met again below itself has no finite
    // selection, so it refuses the machine, naming the type.
    private static string Selection(IType type, List<string> ancestors, IMachine machine)
    {
        var named = type.NamedType();
        switch (named)
        {
            case IObjectTypeDefinition objectType:
                if (ancestors.Contains(objectType.Name, StringComparer.Ordinal))
                    throw new InvalidOperationException(
                        $"The machine '{machine.Name}' answers with the type {objectType.Name}, which holds itself "
                            + $"({string.Join(" > ", ancestors.Append(objectType.Name))}), so no finite document "
                            + "selects it. Give the record a shape that ends."
                    );
                ancestors.Add(objectType.Name);
                var fields = objectType
                    .Fields.Where(f =>
                        !f.Name.StartsWith("__", StringComparison.Ordinal)
                        && f.Arguments.All(a =>
                            a.Type.Kind != TypeKind.NonNull || a.DefaultValue is not null
                        )
                    )
                    .Select(f => f.Name + Selection(f.Type, ancestors, machine))
                    .ToList();
                ancestors.RemoveAt(ancestors.Count - 1);
                return fields.Count == 0
                    ? " { __typename }"
                    : " { " + string.Join(" ", fields) + " }";
            case IInterfaceTypeDefinition or IUnionTypeDefinition:
                return " { __typename }";
            default:
                return "";
        }
    }

    // A type as a variable definition writes it: [Name!]!.
    private static string Print(IType type) =>
        type.Kind switch
        {
            TypeKind.NonNull => Print(type.InnerType()) + "!",
            TypeKind.List => "[" + Print(type.InnerType()) + "]",
            _ => type.NamedType().Name,
        };
}

/// <summary>One generated machine document, as <see cref="MachineDocuments.Generate"/> returns it.</summary>
/// <param name="Id">The persisted id: the reserved prefix, the machine, the operation and the hash.</param>
/// <param name="Machine">The machine's id.</param>
/// <param name="Operation">The operation's name (<c>start</c>, <c>discard</c>, <c>get</c> or an event).</param>
/// <param name="Text">The GraphQL document text.</param>
/// <param name="IrHash">The hash of the machine's IR the document was generated for.</param>
internal sealed record MachineDocument(
    string Id,
    string Machine,
    string Operation,
    string Text,
    string IrHash
) : GeneratedDocument(Id, Text)
{
    /// <inheritdoc />
    public override string Slot => Machine;

    /// <inheritdoc />
    public override string Owner => $"machine '{Machine}'";

    /// <inheritdoc />
    public override string Description =>
        $"Generated for the machine '{Machine}' ({Operation}), IR {IrHash}.";

    /// <inheritdoc />
    public override bool Matches(string stored) =>
        string.Equals(
            MachineDocuments.IdOf(Machine, Operation, IrHash, stored),
            Id,
            StringComparison.Ordinal
        );
}
