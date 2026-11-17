using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.StateMachine;
using SnapshotDraft = Trax.Effect.Models.SnapshotDraft.SnapshotDraft;
using SnapshotTransition = Trax.Effect.Models.SnapshotTransition.SnapshotTransition;

namespace Trax.StateMachine.Persistence;

/// <summary>When a user's draft has expired, as one pass of the draft sweep reads it.</summary>
/// <param name="IdleBefore">
/// A draft last written before this, with no timer armed and no invoked run, has expired, unless its state is
/// <paramref name="Kept"/>; null for no TTL.
/// </param>
/// <param name="BornBefore">
/// A draft created before this (or, with no creation time recorded, last written before it) has expired whatever its
/// timers or its run say, unless its state is <paramref name="Kept"/>; null for no bound.
/// </param>
/// <param name="Kept">
/// For a row that does not record whether the server put it in its state, the states both
/// <paramref name="IdleBefore"/> and <paramref name="BornBefore"/> leave alone, each as <see cref="Key"/> writes it; null
/// keeps none. A row that records it is kept when the server put it there, whatever its state.
/// </param>
internal readonly record struct DraftExpiry(
    DateTimeOffset? IdleBefore,
    DateTimeOffset? BornBefore,
    IReadOnlyCollection<string>? Kept = null
)
{
    /// <summary>Between a machine's name and a state's in <see cref="Key"/>; in neither, being a control character.</summary>
    internal const string Separator = "\u001f";

    /// <summary>One machine's state, as <see cref="Kept"/> lists it.</summary>
    internal static string Key(string machine, string state) => machine + Separator + state;
}
