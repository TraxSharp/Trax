using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Effect.Configuration.Deploy;

/// <summary>The database a deployed host needs.</summary>
public sealed class DeployDatabase
{
    /// <summary><c>postgres</c>, <c>sqlite</c> or <c>in-memory</c>.</summary>
    public string Provider { get; set; } = "";

    /// <summary>
    /// Whether the host migrates the schema when it starts. False when its code calls <c>SkipMigrations()</c>; a
    /// deployment that runs <c>--trax-migrate</c> sets <c>TRAX_MIGRATIONS=skip</c> on the rest regardless.
    /// </summary>
    public bool MigratesOnStart { get; set; }
}
