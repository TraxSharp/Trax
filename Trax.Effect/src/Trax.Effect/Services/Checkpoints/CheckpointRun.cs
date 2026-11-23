using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Core.Exceptions;
using Trax.Core.Monad;
using Trax.Effect.Services.JunctionEvents;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Utils;

namespace Trax.Effect.Services.Checkpoints;

/// <summary>
/// What the store needs to know about the run on this flow: its row, and its chain's hash, read only
/// when the run reaches a checkpoint, so a train that declares none never has its chain read. Set by
/// <c>ServiceTrain.Run</c> for the run and everything it awaits.
/// </summary>
internal sealed class CheckpointRun(long metadataId, Func<string?> chainHash)
{
    private readonly Lazy<string?> _chainHash = new(chainHash);

    /// <summary>The run's row.</summary>
    public long MetadataId { get; } = metadataId;

    /// <summary>The run's chain hash, or null when its chain cannot be read.</summary>
    public string? ChainHash => _chainHash.Value;

    /// <summary>True once the run stored a checkpoint, so a completed run knows it has some to delete.</summary>
    public bool Wrote { get; set; }

    private static readonly AsyncLocal<CheckpointRun?> Run = new();

    /// <summary>The run on this flow, or null outside a run that can take checkpoints.</summary>
    public static CheckpointRun? Current
    {
        get => Run.Value;
        set => Run.Value = value;
    }
}
