using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HotChocolate;
using HotChocolate.Types;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;
using Trax.StateMachine.Persistence;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

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
