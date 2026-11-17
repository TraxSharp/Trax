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

/// <summary>A row a <c>start</c> created: its id, the fingerprint of the input it was started with, and the row as stored.</summary>
internal sealed record StartedRow(Guid Id, string? Fingerprint, StoredSnapshot Stored);
