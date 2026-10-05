---
layout: default
title: TraxCaller
description: "Reference for TraxCaller, the caller context that never throws: IsAuthenticated, IsTrusted, Principal, its registration, and when to use it over TraxPrincipal."
parent: API Auth
grand_parent: SDK Reference
---

# TraxCaller

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

The current caller, safe to inject anywhere. Where an injected [`TraxPrincipal`](/docs/sdk-reference/api-auth/injecting-trax-principal)
throws `TraxPrincipalNotAvailableException` for an anonymous caller, `TraxCaller` reports what it
finds and never throws, so it fits code that runs for anonymous and authenticated callers alike:
row-level query filters, subscription resolvers, junctions shared between gated and ungated trains.

## Signature

```csharp
namespace Trax.Api.Auth;

public sealed class TraxCaller
{
    public TraxCaller(IHttpContextAccessor httpContextAccessor, ITrustedExecutionScope trustedScope);

    public bool IsAuthenticated { get; }   // Principal is not null
    public bool IsTrusted { get; }         // inside ITrustedExecutionScope.BeginTrusted(...)
    public TraxPrincipal? Principal { get; }
}
```

| Member | Value |
|---|---|
| `Principal` | The request's [`TraxPrincipal`](/docs/sdk-reference/api-auth/trax-principal), read from `HttpContext.User`, or `null` when there is no `HttpContext`, no authenticated user, or a user whose claims carry no Trax principal id. Its `Id` is qualified by the scheme, `TraxApiKey:alice`. |
| `IsAuthenticated` | `Principal is not null` |
| `IsTrusted` | True inside a trusted execution scope: the scheduler running queued work, a remote runner's run endpoint. Never set by anything on the API's own HTTP surface. |

`Principal` is read on every access, not when `TraxCaller` is built. A principal that authenticates
later in the request (Trax's query-model interceptor authenticates multi-scheme hosts just before
a GraphQL request executes) is visible to code that reads it afterwards, which is what lets an EF
query filter read it when the query runs.

On a subscription, the socket's principal is set on the connection's `HttpContext.User` at
`connection_init`, so `TraxCaller` injected into a subscribe resolver sees the subscriber.

## Registration

`TraxCaller` is a scoped service registered by `AddTraxPrincipalAccessor()`, which every Trax auth
scheme calls (`AddTraxApiKeyAuth`, `AddTraxJwtAuth`, `AddTraxOidcAuth`). A host that registers its
scheme conditionally, such as demo keys only in Development, has no scheme in other environments,
and anything injecting `TraxCaller` then fails to resolve. Register the accessor directly; it is
idempotent:

```csharp
using Trax.Api.Auth;

if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys => keys.Add("member-key-do-not-use-in-production", id: "member", "Member"));

// TraxCaller resolves in every environment, and reports an anonymous caller where no scheme exists.
builder.Services.AddTraxPrincipalAccessor();
```

## Usage

A row-level filter's caller, bound over `TraxCaller` so the data layer does not depend on Trax auth:

```csharp
public sealed class TraxLendingCaller(TraxCaller caller) : ILendingCaller
{
    public string? PrincipalId => caller.Principal?.Id;
    public bool IsLibrarian => caller.Principal?.Roles.Contains("Librarian") == true;
}
```

A subscription resolver that admits only a room's participants:

```csharp
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

Inject `TraxPrincipal` instead when the caller must be authenticated and a missing one is a
configuration mistake you want to fail loudly, such as a junction of a `[TraxAuthorize]` train.

`IsTrusted` is true for whatever a scheduler runner's posture admits, so a filter that lets trusted
execution see every row treats that caller as the scheduler. Give a runner a posture that admits only
the scheduler; see [The Scheduler and Remote Workers Are Trusted](/docs/authorization#the-scheduler-and-remote-workers-are-trusted).

## Package

```
dotnet add package Trax.Api.Auth
```
