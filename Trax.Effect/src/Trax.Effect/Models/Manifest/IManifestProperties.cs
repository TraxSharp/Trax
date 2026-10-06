namespace Trax.Effect.Models.Manifest;

/// <summary>
/// Marker for a train input that can be stored on a manifest. A scheduled train's input type
/// implements it so the scheduler can serialize it into <see cref="Manifest.Properties"/> and
/// rebuild it for each run.
/// </summary>
/// <remarks>
/// The type must round-trip through <c>System.Text.Json</c>, and its FullName must resolve in the
/// process that runs the manifest, because <see cref="Manifest.PropertyTypeName"/> is looked up
/// by name across the loaded assemblies.
/// </remarks>
public interface IManifestProperties { }
