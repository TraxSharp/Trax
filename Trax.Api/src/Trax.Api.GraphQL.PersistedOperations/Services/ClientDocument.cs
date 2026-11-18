using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HotChocolate;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Utilities;
using Trax.Api.GraphQL.Validation;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>One client document, as <see cref="ClientDocuments.LoadAsync"/> reads it.</summary>
/// <param name="Id">The persisted id.</param>
/// <param name="Text">The GraphQL document text, exactly as the file holds it.</param>
/// <param name="Name">The operation's name.</param>
/// <param name="OwnerText">What it is, for a refusal or a log line (<c>client document 'OrderCard' (src/...)</c>).</param>
internal sealed record ClientDocument(string Id, string Text, string Name, string OwnerText)
    : GeneratedDocument(Id, Text)
{
    /// <inheritdoc />
    public override string Slot => ClientDocuments.SlotOf(Name);

    /// <inheritdoc />
    public override string Owner => OwnerText;

    /// <inheritdoc />
    public override string Description => $"Written by the frontend: the {OwnerText}.";

    /// <inheritdoc />
    public override bool Matches(string stored) =>
        string.Equals(ClientDocuments.IdOf(Name, stored), Id, StringComparison.Ordinal);
}
