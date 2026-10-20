---
title: TraxCaller
description: "Reference for TraxCaller, the caller context that never throws: Principal, IsAuthenticated and IsTrusted, how each is read, and where it is registered."
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 18
package: Trax.Api.Auth
namespace: Trax.Api.Auth
source:
  - Trax.Api/src/Trax.Api.Auth/TraxCaller.cs
---

# TraxCaller

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/security/secure-a-graphql-host).

The current caller, as a scoped service that never throws. Where an injected [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal) throws `TraxPrincipalNotAvailableException` for an anonymous caller, `TraxCaller` reports `null`, so it fits code that runs for anonymous and authenticated callers alike: row-level query filters, subscription resolvers, junctions shared by gated and ungated trains.

## Signature

```csharp fragment
public sealed class TraxCaller
{
    public TraxCaller(IHttpContextAccessor httpContextAccessor, ITrustedExecutionScope trustedScope);

    public bool IsAuthenticated { get; }
    public bool IsTrusted { get; }
    public TraxPrincipal? Principal { get; }
}
```

## Properties

| Property | Value |
|---|---|
| `Principal` | The request's `TraxPrincipal`, read from `HttpContext.User`, or `null` when there is no `HttpContext`, no user, or a user whose claims carry no `trax:principal-id`. Its `Id` is qualified by the scheme, `TraxApiKey:alice`. |
| `IsAuthenticated` | `Principal is not null`. |
| `IsTrusted` | `ITrustedExecutionScope.IsTrusted`: true inside a trusted execution scope, such as the scheduler running queued work or a remote runner's run endpoint. Nothing on the API's own HTTP surface sets it. |

## Behavior

`Principal` is read on every access, not when `TraxCaller` is built. A principal that authenticates later in the request is visible to code that reads it afterwards: Trax's query-model interceptor authenticates a multi-scheme host just before a GraphQL request executes, and an EF query filter that reads `Principal` when the query runs sees it.

On a subscription the socket's principal is set on the connection's `HttpContext.User` at `connection_init`, so a `TraxCaller` injected into a subscribe resolver sees the subscriber.

`IsTrusted` is true for whatever a scheduler runner's posture admits, so a filter that lets trusted execution see every row treats that caller as the scheduler. See [The scheduler and remote workers are trusted](/docs/security/authorization-rules#the-scheduler-and-remote-workers-are-trusted).

`TraxCaller` is registered scoped by [AddTraxPrincipalAccessor](/docs/sdk-reference/api-auth/add-trax-principal-accessor), which every `AddTrax*Auth` call runs. A host with no scheme registered in some environment calls `AddTraxPrincipalAccessor()` itself, or `TraxCaller` does not resolve there.

## Examples

A row-level filter's caller, bound over `TraxCaller` so the data layer does not depend on Trax auth:

```csharp compile
public interface ILendingCaller
{
    string? PrincipalId { get; }
    bool IsLibrarian { get; }
}

public sealed class TraxLendingCaller(TraxCaller caller) : ILendingCaller
{
    public string? PrincipalId => caller.Principal?.Id;
    public bool IsLibrarian => caller.Principal?.Roles.Contains("Librarian") == true;
}
```

A subscribe resolver that admits only a room's participants:

```csharp fragment
public async ValueTask<ISourceStream<ChatEvent>> SubscribeToChatEventAsync(
    Guid chatRoomId, TraxCaller caller, ChatDbContext db,
    ITopicEventReceiver receiver, CancellationToken ct)
{
    var userId = caller.Principal?.Id;
    if (userId is null || !await db.Participants.AnyAsync(p => p.RoomId == chatRoomId && p.UserId == userId, ct))
        throw new GraphQLException(ErrorBuilder.New().SetMessage("Not authorized.").SetCode("TRAX_AUTHORIZATION").Build());
    return await receiver.SubscribeAsync<ChatEvent>($"ChatRoom:{chatRoomId}", ct);
}
```

## See also

- [Inject the current principal](/docs/security/inject-the-current-principal)
- [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal), [AddTraxPrincipalAccessor](/docs/sdk-reference/api-auth/add-trax-principal-accessor)
