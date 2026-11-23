namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>The reserved prefixes of generated documents, and the slot an id belongs to.</summary>
internal static class GeneratedDocuments
{
    /// <summary>Whether <paramref name="id"/> is under a prefix only generated documents use.</summary>
    public static bool IsReserved(string? id) =>
        MachineDocuments.IsReserved(id)
        || TrainDocuments.IsReserved(id)
        || ProgressDocuments.IsReserved(id)
        || ViewDocuments.IsReserved(id)
        || ClientDocuments.IsReserved(id)
        || ConsoleDocuments.IsReserved(id);

    /// <summary>
    /// Whether <paramref name="id"/> is a document named by the hash of its text, a client's or the console's: such a
    /// document runs on any node while the store holds it active, whatever its version, once this node's own checks
    /// accept its text.
    /// </summary>
    public static bool IsContentAddressed(string? id) =>
        ClientDocuments.IsReserved(id) || ConsoleDocuments.IsReserved(id);

    /// <summary>The reserved prefix <paramref name="id"/> is under, or null.</summary>
    public static string? PrefixOf(string id) =>
        MachineDocuments.IsReserved(id) ? MachineDocuments.ReservedIdPrefix
        : TrainDocuments.IsReserved(id) ? TrainDocuments.ReservedIdPrefix
        : ProgressDocuments.IsReserved(id) ? ProgressDocuments.ReservedIdPrefix
        : ViewDocuments.IsReserved(id) ? ViewDocuments.ReservedIdPrefix
        : ClientDocuments.IsReserved(id) ? ClientDocuments.ReservedIdPrefix
        : ConsoleDocuments.IsReserved(id) ? ConsoleDocuments.ReservedIdPrefix
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
    /// of them: a machine id's machine, or the slot of any other generated id.
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
        if (ViewDocuments.IsReserved(id))
            return
                ViewDocuments.SlotOf(id) is { } slot && slots.Contains(slot, StringComparer.Ordinal)
                ? slot
                : null;
        if (ClientDocuments.IsReserved(id))
            return
                ClientDocuments.SlotOfId(id) is { } slot
                && slots.Contains(slot, StringComparer.Ordinal)
                ? slot
                : null;
        if (ConsoleDocuments.IsReserved(id))
            return
                ConsoleDocuments.SlotOfId(id) is { } slot
                && slots.Contains(slot, StringComparer.Ordinal)
                ? slot
                : null;
        return MachineDocuments.MachineOf(id, slots);
    }
}
