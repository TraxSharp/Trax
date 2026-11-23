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
