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

/// <summary>A user's row whose pending effect's lease has passed, as the reconciler read it.</summary>
internal sealed record AbandonedEffect(
    long RowId,
    string UserKey,
    string Machine,
    Guid Id,
    string? Trigger
);
