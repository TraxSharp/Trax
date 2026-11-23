using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// What a draft service of a machine that invokes trains writes through: the owner-aware store over the request's
/// data context, and the outbox that writes a snapshot together with the runs it queues or cancels.
/// </summary>
/// <param name="Store">
/// The owner-aware store the draft service reads and expires drafts through, over the request's data context.
/// </param>
/// <param name="Outbox">Writes a snapshot entering or leaving an invoking state in one transaction.</param>
internal sealed record InvokeRuntime(IMachineInstanceStore Store, InvokeOutbox Outbox);
