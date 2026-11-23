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

/// <summary>A claim of the earlier effect-claim ledger, as a row's effect key names it.</summary>
/// <param name="Key">The effect key.</param>
/// <param name="Receipt">The recorded receipt, or null while the claim is in flight.</param>
/// <param name="ContentFingerprint">The fingerprint of the content the effect ran on, or null when none was recorded.</param>
/// <param name="LeaseLive">Whether the claim's lease has not passed yet, on the database's clock.</param>
internal sealed record LedgerClaim(
    string Key,
    string? Receipt,
    string? ContentFingerprint,
    bool LeaseLive
);
