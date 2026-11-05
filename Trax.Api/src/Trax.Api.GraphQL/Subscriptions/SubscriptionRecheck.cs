using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// The authorization a subscription was admitted on, run again every <see cref="Interval"/> while
/// the subscription is open: the connection's credential re-check interval, five minutes unless the
/// GraphQL builder set another with <c>RecheckConnectionCredentialsEvery(...)</c>.
/// </summary>
/// <remarks>
/// Each check asks the endpoint policy set with <c>RequireAuthorization(...)</c> first, when there
/// is one, and then the subscription's own rule (<see cref="For"/>, <see cref="Of"/>). A
/// subscription that no longer passes ends: <see cref="Until{T}"/> sends the subscriber its feed's
/// last event when the feed has one (a machine push and a followed run end with <c>NOT_FOUND</c>),
/// and otherwise a <c>TRAX_AUTHORIZATION</c> error, after which the operation completes. A check
/// that throws counts as one that did not pass. The socket stays open, and its other operations go
/// on. See
/// <c>docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md</c>.
/// </remarks>
/// <param name="Interval">How long a subscription runs between two checks.</param>
/// <param name="Time">The clock the interval is measured on.</param>
/// <param name="StillAuthorized">The check: true while the subscriber may keep receiving.</param>
internal sealed record SubscriptionRecheck(
    TimeSpan Interval,
    TimeProvider Time,
    Func<CancellationToken, ValueTask<bool>> StillAuthorized
)
{
    /// <summary>
    /// A re-check of the endpoint policy and <paramref name="stillAuthorized"/> for
    /// <paramref name="user"/>, at the interval, on the clock and under the endpoint policy the
    /// host registered in <paramref name="services"/>.
    /// </summary>
    public static SubscriptionRecheck For(
        IServiceProvider services,
        ClaimsPrincipal? user,
        Func<CancellationToken, ValueTask<bool>> stillAuthorized
    ) =>
        Of(
            services.GetService<GraphQLConfiguration>(),
            services.GetService<TimeProvider>() ?? TimeProvider.System,
            services.GetRequiredService<IServiceScopeFactory>(),
            user,
            stillAuthorized
        );

    /// <summary>
    /// A re-check of the endpoint policy <paramref name="configuration"/> names and
    /// <paramref name="stillAuthorized"/> for <paramref name="user"/>, at the configuration's
    /// interval. The endpoint policy is evaluated in a scope of its own from
    /// <paramref name="scopes"/>, as it is for a request, so a handler that reads a store reads it
    /// again.
    /// </summary>
    public static SubscriptionRecheck Of(
        GraphQLConfiguration? configuration,
        TimeProvider time,
        IServiceScopeFactory scopes,
        ClaimsPrincipal? user,
        Func<CancellationToken, ValueTask<bool>> stillAuthorized
    ) =>
        new(
            configuration?.ConnectionCredentialRecheckInterval
                ?? TraxCompositeSocketInterceptor.DefaultCredentialRecheckInterval,
            time,
            async ct =>
                await EndpointPolicyHoldsAsync(configuration, scopes, user).ConfigureAwait(false)
                && await stillAuthorized(ct).ConfigureAwait(false)
        );

    private static async ValueTask<bool> EndpointPolicyHoldsAsync(
        GraphQLConfiguration? configuration,
        IServiceScopeFactory scopes,
        ClaimsPrincipal? user
    )
    {
        if (configuration?.AuthorizationRequired != true)
            return true;

        await using var scope = scopes.CreateAsyncScope();
        return await EndpointPolicy
            .IsSatisfiedAsync(
                scope.ServiceProvider.GetRequiredService<IAuthorizationService>(),
                configuration,
                user
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The events of <paramref name="source"/>, until a check fails. Then the subscriber is sent
    /// <paramref name="last"/>'s event when it is given, or a <c>TRAX_AUTHORIZATION</c> error, and
    /// the stream ends.
    /// </summary>
    public async IAsyncEnumerable<T> Until<T>(
        IAsyncEnumerable<T> source,
        Func<T>? last = null,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var events = source.GetAsyncEnumerator(stop.Token);
        Task<bool>? pending = null;
        var refused = false;
        try
        {
            var due = SocketConnectionLifetime.Add(Time.GetUtcNow(), Interval);
            while (true)
            {
                pending ??= events.MoveNextAsync().AsTask();

                var wait = due - Time.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    using var timer = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    var delay = Task.Delay(wait, Time, timer.Token);
                    var first = await Task.WhenAny(pending, delay).ConfigureAwait(false);
                    await timer.CancelAsync().ConfigureAwait(false);
                    if (first == pending)
                    {
                        var more = await pending.ConfigureAwait(false);
                        pending = null;
                        if (!more)
                            yield break;
                        yield return events.Current;
                        continue;
                    }
                    ct.ThrowIfCancellationRequested();
                    if (Time.GetUtcNow() < due)
                        continue;
                }

                if (!await PassesAsync(ct).ConfigureAwait(false))
                {
                    refused = true;
                    break;
                }
                due = SocketConnectionLifetime.Add(Time.GetUtcNow(), Interval);
            }
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            if (pending is null || pending.IsCompleted)
            {
                await events.DisposeAsync().ConfigureAwait(false);
                stop.Dispose();
            }
            else
            {
                // An async iterator cannot be disposed while it is being read; it is disposed once
                // the read it is in answers the cancellation.
                _ = DisposeAfterAsync(pending, events, stop);
            }
        }

        if (!refused)
            yield break;
        if (last is null)
            throw new SubscriptionNoLongerAuthorizedException();
        yield return last();
    }

    private async ValueTask<bool> PassesAsync(CancellationToken ct)
    {
        try
        {
            return await StillAuthorized(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // A check that cannot answer does not admit: the subscriber may subscribe again.
            return false;
        }
    }

    private static async Task DisposeAfterAsync<T>(
        Task<bool> pending,
        IAsyncEnumerator<T> events,
        CancellationTokenSource stop
    )
    {
        try
        {
            await pending.ConfigureAwait(false);
        }
        catch
        {
            // Cancelled or failed: either way the read is over.
        }
        try
        {
            await events.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            stop.Dispose();
        }
    }
}
