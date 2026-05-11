namespace Trax.Api.GraphQL.PersistedOperations;

/// <summary>
/// Marker registered in DI by <c>UsePersistedOperations(...)</c>. Consumers
/// (the Trax dashboard, custom admin UIs) probe for this via
/// <c>IServiceProvider.GetService&lt;IPersistedOperationsCapability&gt;()</c>
/// to decide whether to expose management UI or fall back to a "not enabled"
/// state. Presence implies the HotChocolate validator, capability marker, and
/// management GraphQL mutations/queries are all wired in.
/// </summary>
public interface IPersistedOperationsCapability { }

internal sealed class PersistedOperationsCapability : IPersistedOperationsCapability { }
