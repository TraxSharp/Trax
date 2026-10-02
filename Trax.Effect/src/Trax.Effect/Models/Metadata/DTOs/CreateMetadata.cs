using System.ComponentModel;

namespace Trax.Effect.Models.Metadata.DTOs;

/// <summary>
/// The starting values <see cref="Metadata.Create"/> needs for a new run's row. Infrastructure used
/// by service trains, the mediator and the scheduler to pre-create a run; not intended to be used
/// directly.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class CreateMetadata
{
    /// <summary>
    /// The train's canonical name, normally the FullName of the interface it is registered under.
    /// Stored as <c>Metadata.Name</c>.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The run's external id, stored as <c>Metadata.ExternalId</c>. Usually a new 32-digit GUID; the
    /// scheduler's job dispatcher passes the work queue entry's <c>ExternalId</c>, so the two
    /// correlate. Not checked for uniqueness, and the column has no unique index.
    /// </summary>
    public required string ExternalId { get; set; }

    /// <summary>
    /// The train's input object, kept in memory on the new metadata (<c>GetInputObject()</c>). It is
    /// not serialized into the <c>Input</c> column here; null is allowed.
    /// </summary>
    public required dynamic? Input { get; set; }

    /// <summary>
    /// The <c>Id</c> of the parent run's metadata when this run is started from inside another
    /// train; null for a top-level run.
    /// </summary>
    public long? ParentId { get; set; }

    /// <summary>
    /// The <c>Id</c> of the manifest this run executes, when the scheduler dispatched it; null for
    /// a run started directly.
    /// </summary>
    public long? ManifestId { get; set; }

    /// <summary>The run whose recorded decisions the new run replays, if any.</summary>
    public long? ReplayDecisionsOf { get; set; }
}
