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

/// <summary>A row whose timer is due, as the sweep read it.</summary>
internal sealed record DueRow(
    long RowId,
    Guid Id,
    string Machine,
    SnapshotOwnerKind OwnerKind,
    string? UserKey,
    int Version,
    string State,
    string Context,
    long Revision,
    string? InvokeToken
)
{
    /// <summary>The row's snapshot as stored, which the machine rehydrates (and migrates) before the timer fires.</summary>
    public string Json =>
        new JsonObject
        {
            ["machine"] = Machine,
            ["version"] = Version,
            ["state"] = State,
            ["context"] = JsonNode.Parse(Context),
        }.ToJsonString();
}
