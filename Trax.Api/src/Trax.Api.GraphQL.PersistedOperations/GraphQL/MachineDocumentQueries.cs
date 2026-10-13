using HotChocolate;
using HotChocolate.Types;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.GraphQL.Queries;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL;

/// <summary>
/// Grafts <c>machineDocuments</c> onto <c>operations</c>: the manifest of the machine documents this node registered,
/// which a generated client reads to send each operation by its id. It is behind the operations gate the host
/// declared on the <c>operations</c> field above it. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
[ExtendObjectType(typeof(OperationsQueries))]
internal sealed class OperationsQueriesMachineDocumentsExtension
{
    /// <summary>
    /// Every machine document this node serves: one per operation of each exposed machine, with the machine's IR hash
    /// and the document's text, ordered by machine and then as the schema declares the operations. It is as open as
    /// the <c>operations</c> field above it, which carries the gate the host declared for the namespace, as
    /// <c>persistedOperations</c> is.
    /// </summary>
    [TraxAllowAnonymous]
    [GraphQLDescription(
        "Every machine document this node serves: one per operation of each exposed machine, with the IR hash of the "
            + "machine it was generated for and its text. A generated client sends each by its id."
    )]
    [GraphQLType<NonNullType<ListType<NonNullType<MachineDocumentType>>>>]
    public IReadOnlyList<MachineDocumentEntry> MachineDocuments(
        [Service] MachineDocumentRegistry registry
    ) =>
        registry
            .Documents.Select(d => new MachineDocumentEntry(
                d.Id,
                d.Machine,
                d.Operation,
                d.IrHash,
                d.Text
            ))
            .ToList();
}

/// <summary>One entry of the machine documents manifest.</summary>
/// <param name="Id">The persisted id a client sends.</param>
/// <param name="Machine">The machine's id.</param>
/// <param name="Operation">The operation: <c>start</c>, <c>discard</c>, <c>get</c> or an event's field name.</param>
/// <param name="IrHash">The hash of the machine's IR the document was generated for.</param>
/// <param name="Document">The document's text, which takes its values as variables.</param>
internal sealed record MachineDocumentEntry(
    string Id,
    string Machine,
    string Operation,
    string IrHash,
    string Document
);

/// <summary>The GraphQL type of a <see cref="MachineDocumentEntry"/>.</summary>
internal sealed class MachineDocumentType : ObjectType<MachineDocumentEntry>
{
    protected override void Configure(IObjectTypeDescriptor<MachineDocumentEntry> descriptor)
    {
        descriptor.Name("MachineDocument");
        descriptor.Description("One persisted document of an exposed machine's operation.");
        descriptor.BindFieldsExplicitly();
        descriptor
            .Field(d => d.Id)
            .Type<NonNullType<StringType>>()
            .Description("The persisted id a client sends.");
        descriptor
            .Field(d => d.Machine)
            .Type<NonNullType<StringType>>()
            .Description("The machine's id.");
        descriptor
            .Field(d => d.Operation)
            .Type<NonNullType<StringType>>()
            .Description("The operation: start, discard, get or an event's field name.");
        descriptor
            .Field(d => d.IrHash)
            .Type<NonNullType<StringType>>()
            .Description("The hash of the machine's IR the document was generated for.");
        descriptor
            .Field(d => d.Document)
            .Type<NonNullType<StringType>>()
            .Description("The document's text, which takes its values as variables.");
    }
}
