using System.Diagnostics.CodeAnalysis;

namespace Trax.Effect.Data.Services.InvokedRunListener;

/// <summary>One listening session opened by <see cref="IInvokedRunListener.SubscribeAsync"/>.</summary>
internal interface IInvokedRunSubscription : IAsyncDisposable
{
    /// <summary>
    /// Completes with the external id of the next invoked run that ended. Throws when the session is lost, for
    /// example when its database connection is closed; dispose it and subscribe again.
    /// </summary>
    Task<string> NextAsync(CancellationToken cancellationToken);
}
