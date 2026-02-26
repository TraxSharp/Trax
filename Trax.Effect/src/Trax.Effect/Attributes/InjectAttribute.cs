namespace Trax.Effect.Attributes;

/// <summary>
/// Marks a property for automatic dependency injection in internal Trax.Effect framework classes.
/// </summary>
/// <remarks>
/// This attribute is used internally by the Trax.Effect framework to inject
/// framework-level services (like IEffectRunner, ILogger, IServiceProvider) into
/// EffectWorkflow instances.
///
/// This attribute is NOT intended for use in user code. Steps should use standard
/// constructor injection for their dependencies.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public class InjectAttribute : Attribute { }
