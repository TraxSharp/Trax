namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// A persisted document the host registers itself: a machine's (<see cref="MachineDocument"/>) or runProgress's
/// (<see cref="ProgressDocument"/>), which it generates, one its frontend wrote (<see cref="ClientDocument"/>), or one the
/// operations console sends (<see cref="ConsoleDocument"/>). Its versions are numbered per <see cref="Slot"/>, so an id stored with a
/// higher number than this node's answers <c>SERVER_OUTDATED</c> and one with a lower number <c>CLIENT_OUTDATED</c>.
/// </summary>
internal abstract record GeneratedDocument(string Id, string Text)
{
    /// <summary>
    /// What the document's versions are numbered under: the machine's id for a machine document, the id without its
    /// hash for the others.
    /// </summary>
    public abstract string Slot { get; }

    /// <summary>What generated it, for a log line or a metric (<c>machine 'order'</c>).</summary>
    public abstract string Owner { get; }

    /// <summary>The description its stored row carries.</summary>
    public abstract string Description { get; }

    /// <summary>Whether <paramref name="stored"/> is the body this document's id names.</summary>
    public abstract bool Matches(string stored);
}
