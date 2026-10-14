namespace Trax.Effect.Attributes;

/// <summary>
/// Marks a property for automatic dependency injection in internal Trax.Effect framework classes.
/// </summary>
/// <remarks>
/// This attribute is used internally by the Trax.Effect framework to inject
/// framework-level services (like IEffectRunner, ILogger, IServiceProvider) into
/// EffectTrain instances.
///
/// This attribute is NOT intended for use in user code. Junctions should use standard
/// constructor injection for their dependencies.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/attributes/inject">SDK reference</seealso>
[AttributeUsage(AttributeTargets.Property)]
public class InjectAttribute : Attribute { }
