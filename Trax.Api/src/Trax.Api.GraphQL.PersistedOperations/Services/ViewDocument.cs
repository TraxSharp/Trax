using HotChocolate;
using HotChocolate.Types;
using Trax.Api.GraphQL.TypeModules;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>One generated view document, as <see cref="ViewDocuments.Generate"/> returns it.</summary>
/// <param name="Id">The persisted id.</param>
/// <param name="Field">The view's field under the <c>views</c> root.</param>
/// <param name="Text">The GraphQL document text.</param>
/// <param name="ShapeHash">The hash of the view's shape the id carries.</param>
internal sealed record ViewDocument(string Id, string Field, string Text, string ShapeHash)
    : GeneratedDocument(Id, Text)
{
    /// <inheritdoc />
    public override string Slot => ViewDocuments.SlotFor(Field);

    /// <inheritdoc />
    public override string Owner => $"view '{Field}'";

    /// <inheritdoc />
    public override string Description => $"Generated for the view '{Field}', shape {ShapeHash}.";

    /// <inheritdoc />
    public override bool Matches(string stored) =>
        string.Equals(
            $"{Slot}_{ShapeHash}{TrainDocuments.TextHash(ShapeHash, stored)}",
            Id,
            StringComparison.Ordinal
        );
}
