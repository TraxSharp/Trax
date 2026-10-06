using Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;
using Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

namespace Trax.Dashboard.Components.Pages.Data;

/// <summary>
/// A persisted-operation change the service saved but could not send to the other nodes comes
/// back with the saved row and a <c>CHANGE_NOT_BROADCAST</c> error beside it. It is not a
/// refusal: the change is the operation's new state. The pages show it as a warning, with the
/// service's own message, rather than as "not changed".
/// </summary>
internal static class NotBroadcastWarning
{
    /// <summary>The notification title for a change that was saved but not broadcast.</summary>
    public const string Title = "Saved, but not sent to every node";

    /// <summary>
    /// The warning to show when every error is <c>CHANGE_NOT_BROADCAST</c>, or <c>null</c> when
    /// the change was refused or succeeded outright.
    /// </summary>
    public static string? From(IReadOnlyList<PersistedOperationError> errors) =>
        errors.Count > 0
        && errors.All(e => e.Code == PersistedOperationNotBroadcastException.CodeValue)
            ? string.Join(" ", errors.Select(e => e.Message))
            : null;
}
