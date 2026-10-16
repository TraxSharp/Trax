namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// A persisted document the host registers itself: a machine's (<see cref="MachineDocument"/>), a train's
/// (<see cref="TrainDocument"/>) or runProgress's (<see cref="ProgressDocument"/>), which it generates, or one its
/// frontend wrote (<see cref="ClientDocument"/>). Its versions are numbered per <see cref="Slot"/>, so an id stored with a
/// higher number than this node's answers <c>SERVER_OUTDATED</c> and one with a lower number <c>CLIENT_OUTDATED</c>.
/// </summary>
internal abstract record GeneratedDocument(string Id, string Text)
{
    /// <summary>
    /// What the document's versions are numbered under: the machine's id for a machine document, the id without its
    /// hash for a train's.
    /// </summary>
    public abstract string Slot { get; }

    /// <summary>What generated it, for a log line or a metric (<c>machine 'order'</c>).</summary>
    public abstract string Owner { get; }

    /// <summary>The description its stored row carries.</summary>
    public abstract string Description { get; }

    /// <summary>Whether <paramref name="stored"/> is the body this document's id names.</summary>
    public abstract bool Matches(string stored);
}

/// <summary>The reserved prefixes of generated documents, and the slot an id belongs to.</summary>
internal static class GeneratedDocuments
{
    /// <summary>Whether <paramref name="id"/> is under a prefix only generated documents use.</summary>
    public static bool IsReserved(string? id) =>
        MachineDocuments.IsReserved(id)
        || TrainDocuments.IsReserved(id)
        || ProgressDocuments.IsReserved(id)
        || ClientDocuments.IsReserved(id);

    /// <summary>The reserved prefix <paramref name="id"/> is under, or null.</summary>
    public static string? PrefixOf(string id) =>
        MachineDocuments.IsReserved(id) ? MachineDocuments.ReservedIdPrefix
        : TrainDocuments.IsReserved(id) ? TrainDocuments.ReservedIdPrefix
        : ProgressDocuments.IsReserved(id) ? ProgressDocuments.ReservedIdPrefix
        : ClientDocuments.IsReserved(id) ? ClientDocuments.ReservedIdPrefix
        : null;

    /// <summary>
    /// Whether a stored row is one a host generated: its id is under a reserved prefix and well formed, and its text
    /// hash is the hash of <paramref name="document"/> with the id's own shape or IR hash. Every generated id is built
    /// that way, so a row uploaded under one of these prefixes before they were reserved fails it, and registration
    /// leaves it alone rather than adopting it.
    /// </summary>
    public static bool IsGenerated(string id, string document)
    {
        if (!IsReserved(id) || !MachineDocuments.IsWellFormed(id))
            return false;
        var hash = id[(id.LastIndexOf('_') + 1)..];
        return string.Equals(
            TrainDocuments.TextHash(hash[..MachineDocuments.IrHashLength], document),
            hash[MachineDocuments.IrHashLength..],
            StringComparison.Ordinal
        );
    }

    /// <summary>
    /// The slot out of <paramref name="slots"/> that <paramref name="id"/> belongs to, or null when it belongs to none
    /// of them: a machine id's machine, or a train id's slot.
    /// </summary>
    public static string? SlotOf(string id, IEnumerable<string> slots)
    {
        if (TrainDocuments.IsReserved(id))
            return
                TrainDocuments.SlotOf(id) is { } slot
                && slots.Contains(slot, StringComparer.Ordinal)
                ? slot
                : null;
        if (ProgressDocuments.IsReserved(id))
            return
                ProgressDocuments.SlotOf(id) is { } slot
                && slots.Contains(slot, StringComparer.Ordinal)
                ? slot
                : null;
        if (ClientDocuments.IsReserved(id))
            return
                ClientDocuments.SlotOfId(id) is { } slot
                && slots.Contains(slot, StringComparer.Ordinal)
                ? slot
                : null;
        return MachineDocuments.MachineOf(id, slots);
    }
}
