using System.Text.Json.Serialization;

namespace Trax.Api.GraphQL.PersistedOperations.Broadcasting;

/// <summary>
/// Discriminator values for <see cref="PersistedOperationChangedMessage.ChangeType"/>.
/// </summary>
public static class PersistedOperationChangeType
{
    /// <summary>Operation was inserted or its document was rewritten.</summary>
    public const string Upsert = "Upsert";

    /// <summary>Operation was soft-deleted.</summary>
    public const string Deactivate = "Deactivate";

    /// <summary>Previously deactivated operation was restored.</summary>
    public const string Restore = "Restore";
}
