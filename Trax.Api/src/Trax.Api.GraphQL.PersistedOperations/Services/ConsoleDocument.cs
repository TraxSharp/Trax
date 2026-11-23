using System.Security.Cryptography;
using System.Text;
using HotChocolate;
using HotChocolate.Language;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>One console document this host serves, as <see cref="ConsoleDocuments.Select"/> returns it.</summary>
/// <param name="Id">The persisted id.</param>
/// <param name="Text">The GraphQL document text, as the console sends it.</param>
/// <param name="Name">The operation's name.</param>
internal sealed record ConsoleDocument(string Id, string Text, string Name)
    : GeneratedDocument(Id, Text)
{
    /// <inheritdoc />
    public override string Slot => ConsoleDocuments.SlotOf(Name);

    /// <inheritdoc />
    public override string Owner => $"console document '{Name}'";

    /// <inheritdoc />
    public override string Description => $"Sent by the operations console: '{Name}'.";

    /// <inheritdoc />
    public override bool Matches(string stored) =>
        string.Equals(ConsoleDocuments.IdOf(Name, stored), Id, StringComparison.Ordinal);
}
