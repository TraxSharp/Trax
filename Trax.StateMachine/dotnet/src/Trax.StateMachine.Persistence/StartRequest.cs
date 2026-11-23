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

/// <summary>The request id and input fingerprint of the <c>start</c> that creates a row.</summary>
/// <param name="RequestId">The client's request id.</param>
/// <param name="Fingerprint">The fingerprint of the start's input.</param>
internal sealed record StartRequest(string RequestId, string Fingerprint);
