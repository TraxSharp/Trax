namespace Trax.Core.Decisions;

/// <summary>
/// One answer. <see cref="Model"/> says which model, at which version, gave it, because a
/// confidence threshold tuned against one version does not carry over to another.
/// </summary>
public abstract record Answer
{
    /// <summary>The model that answered, as the decider named it, or null for a decider that is not a model.</summary>
    public string? Model { get; init; }
}
