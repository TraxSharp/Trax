using System.Text.Json.Serialization;

namespace Trax.Api.GraphQL.PersistedOperations.Broadcasting;

/// <summary>
/// Payload broadcast on every persisted-operation mutation. Subscribers use
/// this to invalidate their local cache entry for <c>(TenantKey, Id)</c>.
/// </summary>
public sealed record PersistedOperationChangedMessage(
    [property: JsonPropertyName("tenantKey")] string? TenantKey,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("changeType")] string ChangeType,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp
);

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
