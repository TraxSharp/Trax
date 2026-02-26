namespace Trax.Effect.Data.Postgres;

/// <summary>
/// Provides a reference point for assembly scanning operations within the Trax.Effect.Data.Postgres library.
/// </summary>
/// <remarks>
/// The AssemblyMarker class serves as an anchor for reflection-based operations that need to locate
/// and load resources from this assembly. It has no functionality of its own and exists solely as a
/// type reference.
///
/// This class is used in several scenarios:
/// 1. By the DatabaseMigrator to locate embedded SQL migration scripts in this assembly
/// 2. For assembly scanning operations that need to discover types in this assembly
/// 3. As a reference point for logging and diagnostics
///
/// Example usage:
/// ```csharp
/// // Get the assembly containing PostgreSQL-specific code
/// var postgresAssembly = typeof(AssemblyMarker).Assembly;
///
/// // Use the assembly for resource loading, type scanning, etc.
/// var embeddedResource = postgresAssembly.GetManifestResourceStream("ResourceName");
/// ```
/// </remarks>
public class AssemblyMarker { }
