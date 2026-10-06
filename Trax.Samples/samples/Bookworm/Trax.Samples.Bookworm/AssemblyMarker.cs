namespace Trax.Samples.Bookworm;

/// <summary>
/// Marker type used by the host to register this assembly with the mediator and the GraphQL type
/// discovery (<c>AddMediator(typeof(AssemblyMarker).Assembly)</c>,
/// <c>AddTypeExtensions(typeof(AssemblyMarker).Assembly)</c>).
/// </summary>
public sealed class AssemblyMarker;
