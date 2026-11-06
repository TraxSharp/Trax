using System.ComponentModel.DataAnnotations.Schema;

namespace Trax.Effect.Models.MachinePolicyFire;

/// <summary>
/// One fire a machine policy still owes (<c>trax.machine_policy_fire</c>): written in the same transaction as the
/// transition it reacts to, so it exists only if that transition committed, and deleted in the transaction that fires
/// it. A row with no <see cref="TargetRowId"/> is the policy's reaction to one transition, whose targets are found when
/// it is fired; a broadcast is expanded into one row per target. It holds rows, ids and names, never a context or an
/// owner's key.
/// </summary>
public class MachinePolicyFire
{
    /// <summary>The row's identity.</summary>
    [Column("id")]
    public long Id { get; set; }

    /// <summary>The policy, by the identity its host file gives it.</summary>
    [Column("policy")]
    public string Policy { get; set; } = null!;

    /// <summary>The row of the instance whose transition set the policy off.</summary>
    [Column("source_row_id")]
    public long SourceRowId { get; set; }

    /// <summary>That instance's id, which a linked target's context holds.</summary>
    [Column("source_instance_id")]
    public Guid SourceInstanceId { get; set; }

    /// <summary>The revision the transition gave the source row.</summary>
    [Column("source_revision")]
    public long SourceRevision { get; set; }

    /// <summary>The instance to fire, once a broadcast is expanded; null until then, and for one target found at fire time.</summary>
    [Column("target_row_id")]
    public long? TargetRowId { get; set; }

    /// <summary>When the row was written.</summary>
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
