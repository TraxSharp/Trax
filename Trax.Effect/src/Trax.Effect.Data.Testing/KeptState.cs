using System.Data.Common;
using System.Globalization;
using System.Reflection;
using Npgsql;
using Trax.Effect.Enums;

namespace Trax.Effect.Data.Testing;

/// <summary>
/// A state the draft sweep keeps a user's instance in however long it is idle or has lived, as
/// <see cref="TraxInvariants"/> checks it: one only the server puts an instance in (a committed state, an effect's
/// target, an invoked train's outcome target, a gated event's target), which is the record of what the server did.
/// </summary>
/// <param name="Machine">The machine's name, as stored in <c>trax.snapshot_draft.machine</c>.</param>
/// <param name="State">The state's name, as stored in <c>trax.snapshot_draft.state</c>.</param>
public sealed record KeptState(string Machine, string State);
