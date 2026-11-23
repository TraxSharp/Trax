using System.Data.Common;
using System.Globalization;
using System.Reflection;
using Npgsql;
using Trax.Effect.Enums;

namespace Trax.Effect.Data.Testing;

/// <summary>A state of a machine that invokes a train, as <see cref="TraxInvariants"/> checks it.</summary>
/// <param name="Machine">The machine's name, as stored in <c>trax.snapshot_draft.machine</c>.</param>
/// <param name="State">The state's name, as stored in <c>trax.snapshot_draft.state</c>.</param>
public sealed record InvokingState(string Machine, string State);
