using System.Runtime.InteropServices;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>The row a write left, as the writer knows it without reading the row back.</summary>
/// <param name="RowId">The row's surrogate key, which names the incarnation it wrote.</param>
/// <param name="Revision">The revision the write gave the row.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct WrittenRow(long RowId, long Revision)
{
    /// <summary>The due time the write left on the row: when its state's timer fires, or null when it has none.</summary>
    public DateTimeOffset? DueAt { get; init; }
}
