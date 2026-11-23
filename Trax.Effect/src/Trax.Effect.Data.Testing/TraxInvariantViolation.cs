using System.Data.Common;
using System.Globalization;
using System.Reflection;
using Npgsql;
using Trax.Effect.Enums;

namespace Trax.Effect.Data.Testing;

/// <summary>One row that breaks one of <see cref="TraxInvariants"/>' checks.</summary>
/// <param name="Invariant">Which check, one of the <see cref="TraxInvariants"/> constants.</param>
/// <param name="Table">The table the row is in, schema-qualified.</param>
/// <param name="Id">The row's id: the run, queue entry, checkpoint or snapshot row id, or the effect key.</param>
/// <param name="Detail">What is wrong with the row, in a sentence.</param>
public sealed record TraxInvariantViolation(
    string Invariant,
    string Table,
    string Id,
    string Detail
)
{
    /// <inheritdoc />
    public override string ToString() => $"[{Invariant}] {Table} {Id}: {Detail}";
}
