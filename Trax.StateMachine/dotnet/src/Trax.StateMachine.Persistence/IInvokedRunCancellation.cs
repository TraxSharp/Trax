using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// Cancels the train run an invoking state queued, given the run's invoke token. A draft that holds a live token is
/// never deleted until this has returned, so draft expiry cannot strand a run. <c>AddStateMachines</c> registers the
/// implementation, which cancels as the operations surface does: a still-queued work queue entry is marked
/// cancelled, and a dispatched run has its cancel flag set, which it reads at its next junction on any host.
/// </summary>
internal interface IInvokedRunCancellation
{
    /// <summary>
    /// Cancels the run whose invoke token is <paramref name="invokeToken"/>, or does nothing when it has already
    /// finished. Throw when the cancel cannot be recorded: the draft is then kept, and its run with it.
    /// </summary>
    /// <param name="invokeToken">The token the draft holds, the queued run's external id.</param>
    /// <param name="cancellationToken">
    /// The draft service passes <see cref="CancellationToken.None"/>: a request that goes away between the cancel
    /// and the delete must not leave either half done.
    /// </param>
    Task Cancel(string invokeToken, CancellationToken cancellationToken = default);
}
