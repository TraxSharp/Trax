namespace Trax.Effect.Attributes;

/// <summary>
/// Opts a train into broadcasting lifecycle events to GraphQL subscribers.
/// Only trains decorated with this attribute will have their lifecycle transitions
/// (started, completed, failed, cancelled) published to WebSocket subscribers.
/// </summary>
/// <remarks>
/// Place this attribute on the concrete train class. Trains without this attribute
/// will not emit subscription events, even if lifecycle hooks are registered.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class TraxBroadcastAttribute : Attribute { }
