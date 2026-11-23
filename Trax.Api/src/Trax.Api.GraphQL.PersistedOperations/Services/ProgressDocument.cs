using HotChocolate;
using HotChocolate.Types;
using Trax.Api.GraphQL.TypeModules;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>One generated runProgress document, as <see cref="ProgressDocuments.Generate"/> returns it.</summary>
/// <param name="Id">The persisted id.</param>
/// <param name="Operation"><c>query</c> or <c>subscription</c>.</param>
/// <param name="Text">The GraphQL document text.</param>
/// <param name="ShapeHash">The hash of the answer's shape the id carries.</param>
internal sealed record ProgressDocument(string Id, string Operation, string Text, string ShapeHash)
    : GeneratedDocument(Id, Text)
{
    /// <inheritdoc />
    public override string Slot => ProgressDocuments.SlotFor(Operation);

    /// <inheritdoc />
    public override string Owner => $"runProgress {Operation}";

    /// <inheritdoc />
    public override string Description =>
        $"Generated for runProgress ({Operation}), shape {ShapeHash}.";

    /// <inheritdoc />
    public override bool Matches(string stored) =>
        string.Equals(
            $"{Slot}_{ShapeHash}{TrainDocuments.TextHash(ShapeHash, stored)}",
            Id,
            StringComparison.Ordinal
        );
}
