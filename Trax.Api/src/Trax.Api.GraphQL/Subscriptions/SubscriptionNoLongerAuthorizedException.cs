using Trax.Api.Exceptions;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Ends a subscription whose authorization no longer passes at a re-check
/// (<see cref="SubscriptionRecheck"/>). The error filter answers it as <c>TRAX_AUTHORIZATION</c>
/// with <see cref="TrainAuthorizationException.PublicMessage"/>, the answer a refused subscribe gets.
/// </summary>
internal sealed class SubscriptionNoLongerAuthorizedException()
    : Exception(TrainAuthorizationException.PublicMessage);
