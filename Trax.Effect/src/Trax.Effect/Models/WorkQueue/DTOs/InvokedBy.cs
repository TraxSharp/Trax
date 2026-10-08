using System.Diagnostics.CodeAnalysis;
using Trax.Effect.Enums;

namespace Trax.Effect.Models.WorkQueue.DTOs;

/// <summary>
/// The state-machine instance whose invoking state queued a run: the machine, the instance (the draft or system
/// instance id) and whether a user or the system owns it. It is written on the run's work queue entry and carried
/// to its metadata at dispatch, so the run stays linked to its instance after the instance has left the state.
/// </summary>
/// <param name="Machine">The machine's id.</param>
/// <param name="InstanceId">The draft or system instance id.</param>
/// <param name="OwnerKind">Whether a user or the system owns the instance.</param>
[Experimental("TRAXEXP002")]
public sealed record InvokedBy(string Machine, Guid InstanceId, SnapshotOwnerKind OwnerKind);
