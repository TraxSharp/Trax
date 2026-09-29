namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Marks a subscription field of Trax's own whose authorization is decided per subscriber by
/// <see cref="LifecycleSubscriptionAccess"/> when it subscribes, rather than by a directive.
/// </summary>
/// <remarks>
/// The type-extension exposure census requires every field on the subscription root, Trax's own
/// included, to declare its posture, and this is how Trax's own fields declare theirs. A new field
/// on <see cref="LifecycleSubscriptions"/> without it, or without <c>[TraxAuthorize]</c> or
/// <c>[TraxAllowAnonymous]</c>, fails the host at startup. See
/// <c>docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class AuthorizedPerSubscriberAttribute : Attribute;
