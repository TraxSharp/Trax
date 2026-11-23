namespace Trax.StateMachine.Persistence;

/// <summary>What <see cref="ISnapshotChangeListener"/> hands its readers.</summary>
internal abstract record SnapshotChange
{
    private SnapshotChange() { }

    /// <summary>The row <paramref name="RowId"/> was inserted, updated or deleted. A hint, never the row.</summary>
    public sealed record Row(long RowId) : SnapshotChange;

    /// <summary>Hints may have been missed: re-read every row you watch.</summary>
    public sealed record Reload : SnapshotChange
    {
        /// <summary>The one instance.</summary>
        public static readonly Reload Instance = new();
    }
}
