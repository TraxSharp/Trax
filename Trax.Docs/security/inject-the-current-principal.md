---
title: Inject the current principal
description: How to read the authenticated caller in junctions and services through TraxPrincipal or TraxCaller, including junctions the scheduler also runs, and in tests.
type: how-to
area: security
nav_order: 10
redirect_from:
  - /docs/sdk-reference/api-auth/injecting-trax-principal
---

# Inject the current principal

Code that needs the authenticated caller (a junction, an application service, a minimal API
handler) takes it from DI. Every `AddTrax*Auth` call registers the injectable services through
[AddTraxPrincipalAccessor](/docs/sdk-reference/api-auth/add-trax-principal-accessor); no
`IHttpContextAccessor` plumbing is needed.

## Inject TraxPrincipal into a gated junction

When the train is gated by `[TraxAuthorize]`, inject [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal)
directly. The authorization check refuses an anonymous caller before the junction is built, so the
principal is always there:

```csharp compile
using Trax.Api.Auth;
using Trax.Core.Junction;

public record SendMessageInput(string Body);

public record SentMessage(string SenderId, string SenderName, string Body);

public class SendMessageJunction(TraxPrincipal user) : Junction<SendMessageInput, SentMessage>
{
    public override Task<SentMessage> Run(SendMessageInput input) =>
        Task.FromResult(new SentMessage(user.Id, user.DisplayName, input.Body));
}
```

`user.Id` is scheme-qualified, such as `TraxApiKey:alice`. Resolving `TraxPrincipal` with no
authenticated Trax principal throws `TraxPrincipalNotAvailableException`; if it does, the train is
missing its gate.

## Junctions the scheduler also runs

A junction that runs from the API and from the scheduler cannot take `TraxPrincipal` in its
constructor: the scheduler path has no `HttpContext`. Use one of these instead.

**A train a machine invokes: inject InvokedRun.** Its input is built from the machine's context,
which the caller does not write, so it cannot carry the initiator. Its junctions inject
[InvokedRun](/docs/sdk-reference/statemachine-api/invoked-run) instead, whose `UserKey` is the
owner of the instance that queued the run, resolved by Trax from the instance's row. Do not put a
token in the context for the train to check: a run's input is kept with the run, and anyone who
reads it could use the token.

**Carry the initiator in the input.** The API side fills it from the principal; the scheduler
replays what was persisted with the work item:

```csharp compile
using Trax.Core.Junction;

public record AddJobInput(string InitiatorUserId, string JobTitle);

public record Job(string OwnerId, string Title);

public class AddJobJunction : Junction<AddJobInput, Job>
{
    public override Task<Job> Run(AddJobInput input) =>
        Task.FromResult(new Job(input.InitiatorUserId, input.JobTitle));
}
```

**Inject TraxCaller.** [TraxCaller](/docs/sdk-reference/api-auth/trax-caller) never throws:
`Principal` is `null` for an anonymous caller, and `IsTrusted` says whether the scheduler or a
runner is executing:

```csharp compile
using Trax.Api.Auth;
using Trax.Core.Junction;

public record ReportInput(string Name);

public record Report(string Name, string RequestedBy);

public class BuildReportJunction(TraxCaller caller) : Junction<ReportInput, Report>
{
    public override Task<Report> Run(ReportInput input) =>
        Task.FromResult(new Report(input.Name, caller.Principal?.Id ?? "system"));
}
```

## When no scheme is registered

If every `AddTrax*Auth` call is conditional, a host where none ran refuses to start, because a
junction needs `TraxPrincipal`. Register the accessor unconditionally; gated trains then refuse
every caller until a scheme is configured:

```csharp compile
using Trax.Api.Auth;

builder.Services.AddAuthentication(); // UseAuthentication() needs it when no scheme ran
builder.Services.AddTraxPrincipalAccessor();
```

## Supply a principal in tests

A test that builds junctions from a service collection registers a fixed principal. The
accessor registers its factory with `TryAdd` and DI resolves the last registration, so the fixed
one wins whichever is registered first:

```csharp compile
using Trax.Api.Auth;

services.AddScoped(_ => new TraxPrincipal("test-user", "Test User", ["Admin"]));
```

## SDK Reference

> [AddTraxPrincipalAccessor](/docs/sdk-reference/api-auth/add-trax-principal-accessor) | [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal) | [TraxCaller](/docs/sdk-reference/api-auth/trax-caller)
