---
title: Authorization rules
description: "The rules Trax applies to GraphQL authorization: the posture every exposed surface declares, how policies and roles combine, and what gates each surface."
type: reference
area: security
nav_order: 2
source:
  - Trax.Effect/src/Trax.Effect/Attributes/TraxAuthorizeAttribute.cs
  - Trax.Effect/src/Trax.Effect/Attributes/TraxAllowAnonymousAttribute.cs
  - Trax.Effect/src/Trax.Effect/Attributes/TraxAuthorization.cs
  - Trax.Api/src/Trax.Api/Services/Authorization/TrainAuthorizationService.cs
  - Trax.Api/src/Trax.Api.GraphQL/Configuration/ExposureAuthorizationRule.cs
  - Trax.Api/src/Trax.Api.GraphQL/Configuration/TypeExtensionExposureRule.cs
  - Trax.Api/src/Trax.Api.GraphQL/Authorization/TraxHttpAuthenticationInterceptor.cs
  - Trax.Mediator/src/Trax.Mediator/Services/TrainAuthorization/ITrainAuthorizationService.cs
---

# Authorization rules

> **Security notice.** NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [Secure a GraphQL host](/docs/api-security).

Every surface the Trax GraphQL endpoint exposes declares who may reach it, and a host whose
declarations are missing or contradictory does not start. This page lists the rules. Why they are
shaped this way is in [Where Trax checks authorization](/docs/security/where-authorization-is-checked);
every refusal they produce is in [Startup refusals](/docs/security/startup-refusals).

## Gates

| Gate | Declared with | Checked |
|---|---|---|
| The whole endpoint | `RequireAuthorization(policy?)` on the GraphQL builder | In the request pipeline, for every operation on HTTP and on a subscription socket |
| The `operations` namespace | `GateOperations(policy, roles)`, `GateOperationsToAuthenticatedUsers()`, or `AllowAnonymousOperations()` | `@authorize` on the `operations` field of both root types |
| A train | `[TraxAuthorize]` on the train class, a base class or an interface | By `ITrainAuthorizationService` when the train is run or queued, before its input is read |
| A query model, or an entity one reaches | `[TraxAuthorize]` on the entity class | `@authorize` on the object type and on the model's `discover` entry field |
| A field a type extension adds | `[TraxAuthorize]` on the resolver method or the extension class | `@authorize` on the field |
| Who receives a train's runs over a subscription | The `[TraxBroadcast]` train's own `[TraxAuthorize]` or `[TraxAllowAnonymous]` | Per subscriber; see [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) |

`[TraxAllowAnonymous]` declares a train, entity or field intentionally public. Both attributes apply
to a class, an interface and a method. `[TraxAuthorize]` may be applied more than once;
`[TraxAllowAnonymous]` may not.

## Required exposure posture

A surface is exposed when it is a `[TraxQuery]`, `[TraxMutation]` or `[TraxBroadcast]` train, or a
`[TraxQueryModel]` entity. Each declares `[TraxAuthorize]` or `[TraxAllowAnonymous]`, directly or
through a base class or interface:

| Endpoint | Neither marker | `[TraxAuthorize]` | `[TraxAllowAnonymous]` | Both |
|---|---|---|---|---|
| Not gated (the default) | refused at startup | gated | public | refused at startup |
| Gated with `RequireAuthorization()` | allowed: the endpoint gate covers it | allowed: the attribute adds its policy and roles | refused at startup: it cannot take effect | refused at startup |

A train with none of `[TraxQuery]`, `[TraxMutation]` and `[TraxBroadcast]` is not reachable over
GraphQL and needs no marker.

### Combining with endpoint-level auth

The endpoint gate and the per-surface attributes both run: the endpoint gate in the request
pipeline first, a train's requirements when it is run or queued, a type's directive when it is
resolved. The endpoint gate that counts for the checks above is the builder's
`RequireAuthorization()`; an ASP.NET Core convention on the mapped route
(`UseTraxGraphQL(configure: e => e.RequireAuthorization())`) is enforced but does not count for
these checks.

## How policies and roles combine

Each `[TraxAuthorize]` is a requirement, and every requirement must pass. Inside one attribute,
`Roles` is a comma-separated list, any one of which is enough.

| Attributes | The caller must |
|---|---|
| `[TraxAuthorize]` | be authenticated |
| `[TraxAuthorize("P")]` | pass policy `P` |
| `[TraxAuthorize(Roles = "A,B")]` | hold role `A` or role `B` |
| `[TraxAuthorize("P", Roles = "A")]` | pass `P` and hold `A` |
| `[TraxAuthorize("P1")] [TraxAuthorize("P2")]` | pass `P1` and `P2` |
| `[TraxAuthorize(Roles = "A")] [TraxAuthorize(Roles = "B")]` | hold `A` and hold `B` |
| `[TraxAuthorize("P")] [TraxAuthorize(Roles = "A")]` | pass `P` and hold `A` |

The rule is the same on a train, a query model, an entity, a resolver method, an extension class
and `GateOperations` (each call is one requirement), and it holds wherever the attributes come
from: a class and its interfaces and base classes, an extension class and its method. A
method-level `[TraxAuthorize(Roles = "Support")]` under a class-level
`[TraxAuthorize(Roles = "Admin")]` requires both roles.

Roles match exactly and case-sensitively, as `ClaimsPrincipal.IsInRole` compares them against
each identity's role claim type: `Roles = "Admin"` does not match a role claim of `admin`. Roles
need no registration. A policy is an ASP.NET Core authorization policy, evaluated with
`IAuthorizationService`, and must be registered; a policy name that is not registered refuses the
host at startup, wherever it is named.

```csharp compile
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("MustBeInternal", policy => policy.RequireClaim("network", "internal"));
});
```

Attribute shape is checked at startup too: an empty or whitespace `Policy`, and a `Roles` list that
holds no role, are refused.

## Per-train authorization

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

// Any authenticated caller.
[TraxQuery]
[TraxAuthorize]
public class WhoAmITrain : ServiceTrain<Unit, UserInfo>, IWhoAmITrain
{
    protected override Task<Either<Exception, UserInfo>> Junctions() =>
        Chain<ReadUserJunction>().Resolve();
}

// The "Admin" policy.
[TraxMutation]
[TraxAuthorize("Admin")]
public class DeleteUserTrain : ServiceTrain<DeleteUserInput, Unit>, IDeleteUserTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<DeleteUserJunction>().Resolve();
}

// The "MustBeInternal" policy, and either the Admin or the Manager role.
[TraxMutation]
[TraxAuthorize("MustBeInternal")]
[TraxAuthorize(Roles = "Admin,Manager")]
public class GenerateReportTrain : ServiceTrain<ReportInput, ReportOutput>, IGenerateReportTrain
{
    protected override Task<Either<Exception, ReportOutput>> Junctions() =>
        Chain<GenerateReportJunction>().Resolve();
}

// Public on purpose.
[TraxQuery(Namespace = "public")]
[TraxAllowAnonymous]
public class LookupAnnouncementTrain
    : ServiceTrain<LookupAnnouncementInput, AnnouncementOutput>,
        ILookupAnnouncementTrain
{
    protected override Task<Either<Exception, AnnouncementOutput>> Junctions() =>
        Chain<LookupAnnouncementJunction>().Resolve();
}
```

Discovery reads `[TraxAuthorize]` and `[TraxAllowAnonymous]` from the implementation class, its
base classes and every interface it implements, so an attribute on `IMyTrain` applies when the
class carries none, and a decorated train keeps the requirements of what it wraps.

`ITrainExecutionService` calls the registered `ITrainAuthorizationService` for every train on
every `RunAsync`, `QueueAsync` and `PrepareAsync`, before the input JSON is read. Every caller-built
run or enqueue goes through it: the GraphQL train fields, the `operations` namespace's
`workQueue.queueTrain`, `workQueue.runTrain`, `requeueExecution` and `resumeExecution`, and the
dashboard. `AddTraxGraphQL` (through `AddTraxApi`) registers the default, `TrainAuthorizationService`,
which decides in this order.

### Fail-closed behavior

| Condition | Result |
|---|---|
| The train has no `[TraxAuthorize]` | Allowed |
| A trusted execution scope is open on the flow | Allowed; the skip is logged at Information with the scope's reason |
| No `HttpContext` | Refused: "No request context and no trusted execution scope." |
| The request user is not authenticated | Refused: "No authenticated user." |
| A required policy does not pass | Refused: "Policy '`P`' not satisfied." |
| The user holds no role of one attribute's `Roles` list | Refused: "User lacks required role. Required one of: ..." |
| Otherwise | Allowed |

A refusal is a `TrainAuthorizationException`. Its `Message` is always "Not authorized."; the reason
above and the train name are on its `Reason` and `TrainName` properties, for logs. GraphQL returns
it with code `TRAX_AUTHORIZATION` and that message, and an `IOperationsService` call surfaces it
the same way. The train does not run.

### The scheduler and remote workers are trusted

Work the scheduler dispatches and a remote worker runs goes through `ITrainBus` or a trusted scope
and is not checked again, and so is a train a system-owned state machine instance starts. A train a
user-owned instance starts is checked against that user. See
[Where Trax checks authorization](/docs/security/where-authorization-is-checked#checked-once-at-submission).

### Opting out for scheduler-only hosts

A registered `[TraxAuthorize]` train with no `ITrainAuthorizationService` refuses the host at
startup, and at run time a call to such a train outside a trusted scope throws
`TrainAuthorizationNotConfiguredException`. A process that discovers gated trains but takes no
submissions, a scheduler-only worker for example, opts out:

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(Program).Assembly)
        .AllowMissingAuthorizationService()));
```

In that process every `[TraxAuthorize]` train runs with no authorization check. An API host never
calls it.

### A custom enforcer

`ITrainAuthorizationService` (in `Trax.Mediator`) has one method,
`Task AuthorizeAsync(TrainRegistration registration, CancellationToken ct)`, which returns to allow
and throws to refuse. It is called for every train, including trains with no `[TraxAuthorize]`,
and inside a trusted scope: the mediator does not check `ITrustedExecutionScope.IsTrusted` first,
so an implementation that should skip trusted work checks it itself. `RequiredPolicies` holds every
policy to pass; `RequiredRoles` is only the union of every attribute's roles, and checking it as
any-of admits a caller one of the attributes refuses. Throw `TrainAuthorizationException` to get
`TRAX_AUTHORIZATION` and the generic message; another exception type reaches GraphQL as an
unmapped error.

A decorator over the default keeps its policy and role checks and adds one:

```csharp compile=enforcer
using Trax.Api.Exceptions;
using Trax.Api.Services.Authorization;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrustedExecution;

public sealed class ActiveTenantAuthorizationService(
    TrainAuthorizationService defaults,
    IHttpContextAccessor httpContextAccessor,
    ITrustedExecutionScope trustedScope
) : ITrainAuthorizationService
{
    public async Task AuthorizeAsync(TrainRegistration registration, CancellationToken ct = default)
    {
        // Policies, roles, and the refusal of a call with no request and no trusted scope.
        await defaults.AuthorizeAsync(registration, ct);

        if (!registration.HasAuthorizeAttribute || trustedScope.IsTrusted)
            return;

        var tenantStatus = httpContextAccessor.HttpContext?.User.FindFirst("tenant_status")?.Value;
        if (tenantStatus != "active")
            throw new TrainAuthorizationException(registration.ServiceTypeName, "Tenant is not active.");
    }
}
```

```csharp compile=enforcer
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.Authorization;
using Trax.Mediator.Services.TrainAuthorization;

// After AddTraxGraphQL, which registers the default; the last registration is the one resolved.
builder.Services.AddTraxGraphQL(graphql => graphql.RequireAuthorization());
builder.Services.AddScoped<TrainAuthorizationService>();
builder.Services.AddScoped<ITrainAuthorizationService, ActiveTenantAuthorizationService>();
```

## Per-model authorization

`[TraxAuthorize]` on a `[TraxQueryModel]` entity puts `@authorize` on its object type and on its
entry field under `discover`. The combinators and role matching are the ones above.

| How the request reaches the entity | What is checked |
|---|---|
| Its entry field, `discover.<namespace>.<field>` | The field's directive, before the resolver. Connection fields such as `totalCount` and `pageInfo` are refused with it, so an unauthorized caller cannot count the rows. |
| A navigation from an ungated parent (`discover.publicOwners.nodes[].privateBooks`) | The type's directive, as each node is completed. The parent's rows are read; the gated branch is replaced by an error and its rows are not returned. |
| A `where` or `order` on any field that passes through a navigation to the entity | The entity's directives, before the query runs. A caller who may read the entity filters and sorts through it; anyone else gets `TRAX_AUTHORIZATION` for the whole field. This holds on a model's entry field, on a type extension's `[UseFiltering]` or `[UseSorting]` resolver, and on a `[UseFiltering]` collection navigation. |

```csharp compile
using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Attributes;

[TraxQueryModel(Namespace = "library")]
[TraxAuthorize(Roles = "Subscriber")]
[Table("premium_articles", Schema = "content")]
public class PremiumArticle
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}
```

A caller who is not a Subscriber, anonymous or not, gets `TRAX_AUTHORIZATION` with the message
"Not authorized." from `discover.library.premiumArticles`, and from any other field whose type is
`PremiumArticle`.

At host start the schema is checked: a gated entity whose type or entry field has lost its
`@authorize`, or an anonymous one that has gained it, through a `ConfigureSchema` callback, refuses
the host.

### Entities a query model reaches

A navigation property, or a public method returning an entity (`GetOwner()`, bound as `owner`),
reaches an entity whether or not it is a query model, and puts its object type, filter input and
sort input in the schema. On an endpoint not gated by `RequireAuthorization()`, such an entity
declares its own posture with `[TraxAuthorize]` or `[TraxAllowAnonymous]` on its class, even
behind a gated model.

```csharp compile
using Trax.Effect.Attributes;

[TraxQueryModel(Namespace = "public")]
[TraxAllowAnonymous]
public class Post
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public Account? Author { get; set; } // not a query model
}

[TraxAuthorize(Roles = "Admin")] // required: Post reaches it
public class Account
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
}
```

The other answers are leaving the navigation out of the model's field set (`BindFields = Explicit`
or `ExposeAs`, which narrow the filter and sort inputs too) and gating the endpoint. A class EF
Core maps as owned is part of the entity that holds it and needs no marker.

## Anonymous access via [TraxAllowAnonymous]

`[TraxAllowAnonymous]` declares a surface intentionally public. On a train it adds no runtime
behaviour: train authorization is enforced by the enforcer, not by a schema directive, and the
attribute only satisfies the exposure check. On an entity it leaves the type without
`@authorize`, so any caller reads it.

### Openness does not cascade

`[TraxAllowAnonymous]` on an entity opens that entity only. A navigation to a gated entity is gated
behind it:

```csharp compile
using Trax.Effect.Attributes;

[TraxQueryModel(Namespace = "public")]
[TraxAllowAnonymous]
public class PublicAnnouncement
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public long? RelatedMatchId { get; set; }
    public MatchRecord? RelatedMatch { get; set; }
}

[TraxQueryModel(Namespace = "matches")]
[TraxAuthorize(Roles = "Admin")]
public class MatchRecord
{
    public long Id { get; set; }
    public string MatchId { get; set; } = "";
}
```

An anonymous `discover.public.publicAnnouncements { title }` succeeds. Selecting
`relatedMatch { matchId }`, filtering on `relatedMatch.matchId` or ordering by it returns
`TRAX_AUTHORIZATION`.

## Fields added by a type extension

An `[ExtendObjectType]` class adds fields to a type Trax owns. A field inherits the gate of its
parent type when the parent has one, and declares its own when it does not:

| Parent type | Marker required on the field |
|---|---|
| Carries `[TraxAuthorize]` | No: it inherits the parent's gate |
| Carries `[TraxAllowAnonymous]` | Yes |
| A root type (`RootQuery`, `RootMutation`, `LifecycleSubscriptions`) | Yes |
| A namespace reached from a root through an ungated field (`DiscoverQueries`, `DispatchMutations`, the per-namespace types under them) | Yes |
| A type under `operations`, gated by `GateOperations(...)` or `RequireAuthorization()` | No: it inherits the namespace's gate |
| A type under `operations`, with `AllowAnonymousOperations()` | Yes |
| A query model's navigation target | As the target's own posture: no under `[TraxAuthorize]`, yes under `[TraxAllowAnonymous]` |
| A type with neither marker | No: it reaches the schema only inside a train's output, whose posture governs |

`[TraxAuthorize]` or `[TraxAllowAnonymous]` on the resolver method declares that field. On the
extension class, the attribute applies to every field the extension contributes, including fields
built from its properties, and not to the type being extended. A subscription field
(`[ExtendObjectType("LifecycleSubscriptions")]`) is a field on a root type and declares.

```csharp compile
using HotChocolate;
using HotChocolate.Types;
using Trax.Effect.Attributes;

[ExtendObjectType(typeof(Article))] // Article is [TraxAllowAnonymous]
public sealed class ArticleExtensions
{
    // Inherits nothing from Article, so it declares.
    [TraxAuthorize(Roles = "Subscriber")]
    public IReadOnlyList<Excerpt> GetExcerpts([Parent] Article article) =>
        [new Excerpt(article.Body[..Math.Min(200, article.Body.Length)])];
}
```

The check runs over the merged schema at host start, so it sees an extension however it was
registered: `AddTypeExtension(s)`, a type module from `AddTypeModule<T>()`, or a `ConfigureSchema`
callback. A field built from a lambda has no member to carry an attribute and is not checked; the
code that builds it gates it.

HotChocolate's `[Authorize]` and `[AllowAnonymous]` (`HotChocolate.Authorization`, and
`HotChocolate.AspNetCore.Authorization` from HotChocolate 15) are refused on such a field or its
extension class, gated parent or not:

```text
GraphQL field 'Article.excerpts' (MyApp.ArticleExtensions.GetExcerpts) declares its authorization
posture with [Authorize], which Trax does not read. Trax owns this vocabulary so a surface declares
the same way wherever it lives and whatever GraphQL server is underneath: use [TraxAuthorize] to
gate it, or [TraxAllowAnonymous] to open it to anonymous callers. ...
```

ASP.NET Core's `[Authorize]` is not refused; it governs endpoints and MVC actions.

## Which scheme authenticates a GraphQL request

`[TraxAuthorize]` and `@authorize` read `HttpContext.User`. No `AddTrax*Auth` call sets a default
authentication scheme, so `AddTraxGraphQL` installs `TraxHttpAuthenticationInterceptor`, which runs
before each GraphQL HTTP execution request:

| Condition | The interceptor |
|---|---|
| The builder calls `RequireAuthorization(policy)` and the policy names schemes | Authenticates with that policy's schemes |
| Otherwise, the request is already authenticated (a default scheme, other middleware) | Leaves it |
| Otherwise | Tries every registered scheme in registration order and keeps the first principal that authenticates |
| No scheme authenticates | Leaves the caller anonymous; every gate refuses it with `TRAX_AUTHORIZATION` |

A credential a scheme rejects counts as no credential. A request carrying an API key and a bearer
token is authenticated by whichever scheme was registered first. The interceptor does not run for
the IDE page or WebSocket upgrades; a subscription authenticates from its `connection_init` payload
through `TraxCompositeSocketInterceptor`; see
[Subscriptions](/docs/sdk-reference/graphql-api/subscriptions).

## The operations surface

| Action | Chooses the train and input | Authorized by |
|---|---|---|
| `workQueue.queueTrain`, `workQueue.runTrain`, `requeueExecution`, `resumeExecution` | The caller | The operations gate, and the train's own `[TraxAuthorize]` through `ITrainExecutionService` |
| `triggerManifest`, `triggerManifestDelayed`, `triggerGroup`, the dead-letter requeues | A manifest | The operations gate only |
| The dashboard's queue dialog, Re-queue, and Run dialog | The dashboard user | The dashboard host's gate. The calls run inside a trusted scope (`"dashboard"`), so the train's `[TraxAuthorize]` is skipped; `OnQueue`, the input cap and, for a queue, `QueueSubjectKey` apply |
| The ManifestManager, and dormant dependents a parent train activates | The system | Nothing further: the work runs inside a scheduled or already authorized train, and skips `QueueSubjectKey` and `OnQueue` |

A run started through `workQueue.runTrain` or the dashboard's Run dialog runs the train's `OnQueue`
hook and the input cap, and is handed to the job submitter the train is routed to; it does not
take its `QueueSubjectKey`, so it is not serialized against other work for that subject. Outside a
trusted scope, a train that declares `QueueSubjectKey` is refused a run and must be queued.

`operations.trains` lists each train's `requiredPolicies` and `requiredRoles`. `requiredRoles` is
the union of every attribute's roles; it does not say which attributes they came from.

## What the attributes do not cover

- **A property on its own.** `[TraxAuthorize]` does not apply to a property. A resolver method on
  the entity or on a train's output type can carry it, and that field is gated on every host. To
  make one column admin-only, expose it through a gated method, use a train, or split the entity
  with `ExposeAs`.
- **Rows.** `[TraxAuthorize]` decides whether a caller may read a type, not which of its rows. Scope
  rows with EF Core query filters that read the caller, such as one built over
  [`TraxCaller`](/docs/sdk-reference/api-auth/trax-caller); see
  [Owner-scoped contexts](/docs/effect/effect-providers/domain-data-contexts#owner-scoped-contexts).
  Key ownership on the `trax:principal-id` claim as it is, `{scheme}:{id}`. Nothing in Trax's
  authorization sees those filters; the
  [owner-scope census](/docs/reference/architecture-guards#the-owner-scope-census) in
  `Trax.Effect.Data.Testing` does.
- **The record a caller names.** A caller who passes a train's gate supplies its input, and so the
  subject key `QueueSubjectKey` derives from it and the priority it queues at. Subject keys are one
  space across all trains. Authorize the record in `OnQueue`, which runs with the caller's identity,
  or scope the key per tenant.

## See also

- [Where Trax checks authorization](/docs/security/where-authorization-is-checked)
- [Startup refusals](/docs/security/startup-refusals)
- [Secure a GraphQL host](/docs/api-security)
- [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize),
  [TraxAllowAnonymous](/docs/sdk-reference/attributes/trax-allow-anonymous),
  [ITrustedExecutionScope](/docs/sdk-reference/mediator-api/i-trusted-execution-scope),
  [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql),
  [Query models](/docs/sdk-reference/graphql-api/query-models)
- The [Auth sample](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/Auth) uses each gate in one host, and its E2E suite proves them.

## SDK Reference

> [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxAllowAnonymous](/docs/sdk-reference/attributes/trax-allow-anonymous) | [ITrustedExecutionScope](/docs/sdk-reference/mediator-api/i-trusted-execution-scope) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [TraxQueryModel](/docs/sdk-reference/graphql-api/query-models)
