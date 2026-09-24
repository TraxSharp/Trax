---
layout: default
title: Authorization
nav_order: 9
section: Guides
---

> **Security notice.** NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

# Authorization

Trax supports three levels of authorization for the API layer:

- **Endpoint-level**: gate all Trax endpoints behind a single policy using the `configure` callback on `UseTraxGraphQL`. This is standard ASP.NET Core endpoint authorization.
- **Per-train**: restrict individual trains using the `[TraxAuthorize]` attribute on the train class. When a request comes in to run or queue a train, Trax checks the attribute against the current HTTP user before executing anything.
- **Per-model**: restrict `[TraxQueryModel]`-exposed entities using the same `[TraxAuthorize]` attribute on the entity class. The directive is enforced at GraphQL type level, so the gate also applies when the entity is reached transitively through a navigation property on an ungated parent.

A surface exposed via GraphQL can also be explicitly opened to anonymous access with `[TraxAllowAnonymous]`. See [Anonymous Access via TraxAllowAnonymous](#anonymous-access-via-traxallowanonymous) below.

Endpoint-level auth answers "can this user reach the Trax API at all?" Per-train auth answers "can this user execute *this particular* train?" Per-model auth answers "can this user read rows of *this particular* type, regardless of how they navigated to it?"

## Required Exposure Posture

Every surface exposed via GraphQL must state its authorization posture explicitly. A `[TraxQuery]`/`[TraxMutation]` train or a `[TraxQueryModel]` entity must declare **either** `[TraxAuthorize]` (gated) **or** `[TraxAllowAnonymous]` (intentionally public). A surface that declares neither fails at host startup, so a forgotten gate can never silently ship as a public endpoint. The check runs in `AddTraxGraphQL` (trains) and `TraxGraphQLBuilder.Build()` (query models); the failure message names every offending type.

The one relaxation is endpoint-level gating: `RequireAuthorization()` on the GraphQL builder covers every surface at the transport layer, so a per-surface marker is no longer required. Because nothing is reachable anonymously under that gate, `[TraxAllowAnonymous]` becomes a contradiction there and is rejected.

| Endpoint posture | neither marker | `[TraxAuthorize]` | `[TraxAllowAnonymous]` | both |
|---|---|---|---|---|
| Open (default) | fails startup (implicitly public) | gated | intentionally public | fails startup (conflict) |
| Gated (`RequireAuthorization()`) | allowed (gate covers it) | allowed (adds finer policy/role) | fails startup (contradiction) | fails startup (conflict) |

`RequireAuthorization()` and `[TraxAuthorize]` are not redundant: the endpoint gate is a single transport-level check ("must be authenticated / satisfy this one policy"), while `[TraxAuthorize]` adds per-surface policy and role requirements on top. They compose as defense in depth.

This rule applies only to surfaces that are actually exposed via GraphQL. A train with no `[TraxQuery]`/`[TraxMutation]` (run only through the scheduler, a remote worker, or `ITrainBus` directly) is never reachable over GraphQL and needs no marker.

### Fields Added by a Type Extension

An `[ExtendObjectType]` class adds a field to a type Trax owns. That field is neither a train nor a query model, so the census above cannot see it, and what gates it in practice is inheritance from the parent type's `@authorize`. That holds right up until the parent has no gate to inherit, and then the field is public because nobody looked.

A field added this way must declare its own posture when the parent gives it nothing:

| Parent type | Marker required on the field |
|---|---|
| Carries `[TraxAuthorize]` | No. It inherits the parent's gate. |
| Carries `[TraxAllowAnonymous]` | **Yes.** It inherits nothing. |
| A schema root type (`RootQuery`, `RootMutation`, `LifecycleSubscriptions`) | **Yes.** There is no parent to inherit from. |
| Neither marker | No. The type reaches the schema only inside another surface's output, and that surface's posture governs. |

`[TraxAuthorize]` and `[TraxAllowAnonymous]` both apply to a method, so a resolver declares its posture directly and Trax emits the matching `@authorize` directive for it. The combinator semantics are the same as on a train or an entity: policies AND, roles union and OR.

```csharp
[ExtendObjectType(typeof(Article))]     // Article is [TraxAllowAnonymous]
public sealed class ArticleExtensions
{
    // Inherits nothing from Article, so it says what it is.
    [TraxAuthorize(Roles = "subscriber")]
    public async Task<IReadOnlyList<Excerpt>> GetExcerpts([Parent] Article article) => ...;
}
```

On the extension **class**, `[TraxAuthorize]` applies to every field that extension contributes. It does not touch the type being extended, so gating an extension of a `[TraxAllowAnonymous]` entity does not re-gate the entity.

A subscription is the same construct: `[ExtendObjectType("LifecycleSubscriptions")]` adds a field to a root type, so every `[Subscribe]` field has to declare.

The check walks the merged schema at host start, so it sees a type extension however it was registered, including one added from a `ConfigureSchema` callback. A field built from a lambda in such a callback has no member to carry an attribute and is skipped; it is gated by the code that builds it.

### Do Not Use HotChocolate's [Authorize] or [AllowAnonymous]

**Trax refuses them.** A surface that declares its posture with `HotChocolate.Authorization.Authorize` or `HotChocolate.Authorization.AllowAnonymous` fails at host startup, with a message naming the Trax replacement:

```
GraphQL field 'Issue.content' (Nwyc.IssueContentExtension.GetContent) declares its authorization
posture with [Authorize], which Trax does not read. Trax owns this vocabulary so a surface
declares the same way wherever it lives and whatever GraphQL server is underneath: use
[TraxAuthorize] to gate it, or [TraxAllowAnonymous] to open it to anonymous callers.
```

This is not a style preference. `[TraxAuthorize]` exists so that a consumer declares authorization once, in one vocabulary, on a train or an entity or a resolver, and Trax translates it into whatever the server underneath needs. Accepting the server's attributes alongside Trax's would mean the same question is answered by two attributes with different combinator rules and different inheritance behaviour, and it would couple consumer code to the dependency `[TraxAuthorize]` exists to hide.

Two concrete differences, not just naming:

- **Class-level placement.** HotChocolate applies a class-level attribute on an `[ExtendObjectType]` to *the type being extended*. On a `[TraxAllowAnonymous]` entity that re-locks the whole entity; on a root type it sets the posture of every operation in the schema. `[TraxAuthorize]` applies to the fields the extension contributes.
- **Reach.** `[TraxAuthorize]` works on a train, an entity, an interface and a resolver. HotChocolate's attribute means nothing on a train, which never reaches the schema as a type.

ASP.NET Core's `[Authorize]` is a different matter and is not banned: it governs endpoints and MVC actions, a surface Trax does not own. `RequireAuthorization()` on the GraphQL endpoint is the supported way to gate the transport.

Migrating is mechanical:

| Replace | With |
|---|---|
| `[Authorize]` | `[TraxAuthorize]` |
| `[Authorize(Policy = "P")]` | `[TraxAuthorize(Policy = "P")]` |
| `[Authorize(Roles = ["a", "b"])]` | `[TraxAuthorize(Roles = "a,b")]` |
| `[AllowAnonymous]` | `[TraxAllowAnonymous]` |

## Per-Train Authorization

Decorate any train class with `[TraxAuthorize]` to declare authorization requirements:

```csharp
using Trax.Effect.Attributes;

// Requires an authenticated user. No specific policy or role.
[TraxAuthorize]
public class WhoAmITrain : ServiceTrain<Unit, UserInfo>, IWhoAmITrain
{
    protected override Task<Either<Exception, UserInfo>> Junctions() =>
        Chain<ReadUserJunction>().Resolve();
}

// Requires the "Admin" authorization policy
[TraxAuthorize("Admin")]
public class DeleteUserTrain : ServiceTrain<DeleteUserInput, Unit>, IDeleteUserTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() => Chain<DeleteUserJunction>().Resolve();
}

// Requires the user to have at least one of the listed roles
[TraxAuthorize(Roles = "Manager,Admin")]
public class GenerateReportTrain : ServiceTrain<ReportInput, ReportOutput>, IGenerateReportTrain
{
    protected override Task<Either<Exception, ReportOutput>> Junctions() => Chain<GenerateReportJunction>().Resolve();
}

// No attribute, no per-train auth check. Valid only because this train is not
// exposed via GraphQL (no [TraxQuery]/[TraxMutation]). A GraphQL-exposed train
// with no marker fails startup (see Required Exposure Posture above).
public class PingTrain : ServiceTrain<PingInput, PongOutput>, IPingTrain
{
    protected override Task<Either<Exception, PongOutput>> Junctions() => Chain<PingJunction>().Resolve();
}
```

The attribute supports two properties:

| Property | Type | Description |
|----------|------|-------------|
| `Policy` | `string?` | Name of an ASP.NET Core authorization policy to evaluate |
| `Roles` | `string?` | Comma-separated list of roles. The user must have at least one. Role comparison is case-insensitive. |

The attribute works on classes, interfaces, and base classes. Trax unions the attributes it finds across the implementation type's interface chain and base chain, so `[TraxAuthorize("Admin")]` on an `IMyTrain` interface is honored even when the implementing class carries no attribute. Decorator-wrapped trains inherit their authorization requirements through the same mechanism.

Role comparison is case-insensitive. `[TraxAuthorize(Roles = "admin")]` matches a principal carrying `ClaimTypes.Role = "Admin"` and vice-versa - both sides are normalized to upper-invariant.

## How Policies and Roles Combine

Multiple attributes and mixed policy/role specifications combine as follows:

| Attribute form | What the user must satisfy |
|----------------|---------------------------|
| `[TraxAuthorize]` (bare) | Be authenticated. No additional policy or role requirement. |
| `[TraxAuthorize("P")]` | Policy `P` must pass. |
| `[TraxAuthorize(Roles = "A,B")]` | Hold role `A` OR role `B`. |
| `[TraxAuthorize("P", Roles = "A")]` | Policy `P` must pass AND hold role `A`. |
| `[TraxAuthorize("P1")] [TraxAuthorize("P2")]` | Both `P1` and `P2` must pass. Policies AND across attributes. |
| `[TraxAuthorize(Roles = "A")] [TraxAuthorize(Roles = "B")]` | Hold role `A` OR role `B`. Roles union OR across attributes. |
| `[TraxAuthorize("P")] [TraxAuthorize(Roles = "A")]` | Policy `P` must pass AND hold role `A`. |

In short: every policy has to pass, and at least one role from the unioned list has to match. A bare attribute requires nothing beyond authentication.

```csharp
[TraxAuthorize("MustBeInternal")]
[TraxAuthorize(Roles = "Admin,Manager")]
public class SensitiveTrain : ServiceTrain<SensitiveInput, Unit>, ISensitiveTrain { ... }
```

The caller must pass the `MustBeInternal` policy and hold either `Admin` or `Manager`.

## Per-Model Authorization

Decorate any `[TraxQueryModel]` entity with `[TraxAuthorize]` to gate the auto-generated GraphQL query. The combinator semantics, role normalization, and inheritance behavior all match the per-train surface above. The only difference is enforcement: Trax attaches HotChocolate's native `@authorize` directive to the generated `ObjectType` *and* to the entry field under `discover`, so the gate fires in two places:

- **Direct entry**: a top-level `discover.<namespace>.<modelField>` request runs the field-level directive before the resolver. Connection-shaped scalars like `totalCount` and `pageInfo` are blocked too; an unauthorized caller cannot enumerate the cardinality of a gated entity.
- **Transitive navigation**: a request that reaches the entity through a navigation property on an ungated parent (`discover.publicOwners.nodes[].privateBooks`) triggers the type-level directive when each child node is materialized. The parent's data is still selected from the database, but the unauthorized response substitutes an error for the gated branch and never returns the row payload to the client.

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Attributes;

[TraxQueryModel(Namespace = "library")]
[TraxAuthorize(Roles = "Subscriber")]
[Table("articles", Schema = "content")]
public class Article
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}
```

Anonymous and non-subscriber callers hitting `discover.library.articles` (or any field elsewhere in the schema whose return type is `Article`) receive a `TRAX_AUTHORIZATION` error with the public message `"Not authorized."`.

### Combinator Semantics

Identical to the per-train surface. The table below repeats them for reference; see [Per-Train Authorization](#per-train-authorization) for the prose explanation.

| Attribute(s) on the entity | Meaning |
|----------------------------|---------|
| `[TraxAuthorize]` (bare) | Be authenticated. |
| `[TraxAuthorize("P")]` | Policy `P` must pass. |
| `[TraxAuthorize(Roles = "A,B")]` | Hold role `A` OR role `B`. |
| `[TraxAuthorize("P1")] [TraxAuthorize("P2")]` | Both `P1` and `P2` must pass. Policies AND across attributes. |
| `[TraxAuthorize(Roles = "A")] [TraxAuthorize(Roles = "B")]` | Hold role `A` OR role `B`. Roles union OR across attributes. |
| `[TraxAuthorize("P")] [TraxAuthorize(Roles = "A")]` | Policy `P` must pass AND hold role `A`. |

### Startup Validation

`QueryModelAuthorizationValidator` runs as a hosted service at host start. It throws if any entity references an authorization policy that has not been registered via `services.AddAuthorization(...)`. This catches typoed policy names (`"AdmnPolicy"`) before the first request rather than turning the gate into a silent deny-all in production. Roles are not validated against the principal store (Trax has no view of what roles can exist); attribute shape (empty or whitespace-only `Policy`, all-empty `Roles` CSV) is validated at `TraxGraphQLBuilder.Build` time.

### Authentication for Per-Model Gating

Per-model `[TraxAuthorize]` enforcement runs HotChocolate's `@authorize` directive, which evaluates against `HttpContext.User`. ASP.NET Core's `UseAuthentication()` middleware only populates `HttpContext.User` from the **default authentication scheme**, so multi-scheme hosts (api-key + JWT, api-key + cookie, etc.) where no scheme is configured as the default would otherwise leave HC seeing an anonymous principal.

Trax handles this automatically. When any registered `[TraxQueryModel]` entity carries `[TraxAuthorize]`, `AddTraxGraphQL` wires a HotChocolate `IHttpRequestInterceptor` (`QueryModelAuthenticationInterceptor`) that runs before each GraphQL HTTP request. The interceptor:

1. Returns early if the request is already authenticated by upstream middleware or endpoint-level `RequireAuthorization`.
2. Otherwise, walks every registered authentication scheme and attempts `AuthenticateAsync` against each. The first successful scheme wins; the resulting principal is assigned to `HttpContext.User` for the duration of the request.
3. If no scheme matches the request's credentials, the principal stays anonymous - gated queries will then reject with `TRAX_AUTHORIZATION`.

The interceptor runs only for GraphQL HTTP execution requests, so the Banana Cake Pop tool page and WebSocket subscription upgrades are not affected. Subscriptions authenticate separately via the per-scheme socket interceptors (`TraxApiKeySocketInterceptor`, `TraxJwtSocketInterceptor`).

No consumer configuration is required.

### Limitations

- **Field-level gating inside an entity is not supported.** `[TraxAuthorize]` on a property is ignored. If `User.email` must be admin-only but `User.displayName` must be public, use a custom train rather than `[TraxQueryModel]`, or split the entity into two types via `ExposeAs`.
- **Row-level filtering is not supported.** `[TraxAuthorize]` answers "can this user read *this type*", not "which rows of this type." For per-row scoping (tenancy, ownership, subscription tier), use EF Core global query filters that read the current `ClaimsPrincipal` from a scoped service.

## Anonymous Access via [TraxAllowAnonymous]

`[TraxAllowAnonymous]` is the explicit opt-in counterpart to `[TraxAuthorize]`. It declares a GraphQL-exposed surface intentionally public and satisfies the [Required Exposure Posture](#required-exposure-posture) rule. It applies to both `[TraxQueryModel]` entities and `[TraxQuery]`/`[TraxMutation]` trains.

On a query-model entity it opens that entity to unauthenticated GraphQL reads (and, as below, mirrors HotChocolate's `[AllowAnonymous]` cascade behavior). On a train it carries no runtime gate of its own (train authorization is enforced imperatively, not via schema directives); its only effect is to declare the train intentionally public so it passes the exposure check.

```csharp
using Trax.Effect.Attributes;

// An intentionally public train.
[TraxQuery(Namespace = "public")]
[TraxAllowAnonymous]
public class LookupAnnouncementTrain
    : ServiceTrain<LookupAnnouncementInput, AnnouncementOutput>, ILookupAnnouncementTrain
{
    protected override Task<Either<Exception, AnnouncementOutput>> Junctions() =>
        Chain<LookupAnnouncementJunction>().Resolve();
}
```

Decorating a `[TraxQueryModel]` entity with it opens that entity to unauthenticated GraphQL reads.

```csharp
using Trax.Effect.Attributes;

[TraxQueryModel(Namespace = "public")]
[TraxAllowAnonymous]
[Table("announcements", Schema = "content")]
public class PublicAnnouncement
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}
```

Any caller, with or without authentication, can read `discover.public.announcements`. Authenticated callers are not excluded; the attribute just removes the gate.

### Cascade Behavior

`[TraxAllowAnonymous]` is local to the decorated entity. A navigation property whose target type carries `[TraxAuthorize]` still enforces that gate, even when reached through the anonymous parent:

```csharp
[TraxQueryModel(Namespace = "public")]
[TraxAllowAnonymous]
public class PublicAnnouncement
{
    public long Id { get; set; }
    public string Title { get; set; } = "";

    public long? RelatedMatchId { get; set; }
    public MatchRecord? RelatedMatch { get; set; }  // gated below
}

[TraxQueryModel(Namespace = "matches")]
[TraxAuthorize(Roles = "Admin")]
public class MatchRecord
{
    public long Id { get; set; }
    public string MatchId { get; set; } = "";
}
```

An anonymous query for `discover.public.announcements { title }` succeeds. The same query selecting `relatedMatch { matchId }` fires the gated child's directive and returns `TRAX_AUTHORIZATION`. The anonymous parent does not propagate openness to its gated children.

### Mutual Exclusion with [TraxAuthorize]

`[TraxAllowAnonymous]` and `[TraxAuthorize]` on the same surface (directly, or via inheritance from a base class or interface) is rejected at host startup with a message naming the type. The two attributes have opposite intents and cannot coexist.

### Contradicts an Endpoint-Level Gate

If the GraphQL endpoint is gated with `RequireAuthorization()`, the HTTP layer rejects unauthenticated callers before HotChocolate ever runs, so a surface can never be reached anonymously. `[TraxAllowAnonymous]` would be a promise the endpoint structurally cannot keep, so declaring it on any exposed surface while the builder opted into `RequireAuthorization()` fails at host startup. Remove one or the other: drop the attribute, or drop the endpoint-level gate if the surface should be publicly reachable. (Standard ASP.NET Core endpoint authorization applied separately on the mapped route, rather than via the Trax builder, does not trip this check, matching HotChocolate's own `[AllowAnonymous]` behavior.)

### Schema Validator Coverage

For query-model entities, the same hosted-service invariant check that asserts `@authorize` is present on gated entities also asserts it is *absent* on `[TraxAllowAnonymous]` entities. A custom `ConfigureSchema` callback that reattaches the directive to an anonymous entity throws at host start with a message naming the entity. The opt-in cannot be silently re-locked.

## Registering Policies

Trax uses standard ASP.NET Core authorization policies. Register them in `Program.cs` the same way you would for any controller or endpoint:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("MustBeInternal", policy =>
        policy.RequireClaim("network", "internal"));
});
```

Trax evaluates these policies at runtime using ASP.NET Core's `IAuthorizationService`. If a train requires a policy that isn't registered, the authorization check fails.

## How It Works

1. `ITrainDiscoveryService` reads `[TraxAuthorize]` and `[TraxAllowAnonymous]` attributes across the implementation, its base chain, and every implemented interface. Roles are normalized to upper-invariant; policies are deduplicated. The requirements (and a `HasAllowAnonymousAttribute` flag) are stored on each `TrainRegistration`.
2. `AddTraxGraphQL` enforces the [Required Exposure Posture](#required-exposure-posture) for every exposed train, and `TraxGraphQLBuilder.Build()` does the same for every `[TraxQueryModel]` entity. Both share one rule: a surface with neither marker (on an open endpoint), both markers, or `[TraxAllowAnonymous]` under `RequireAuthorization()` fails startup with a message naming the offending types.
3. At host start, `AuthorizationRegistrationValidator` runs as a hosted service. It throws if any train carries `[TraxAuthorize]` but no `ITrainAuthorizationService` is registered (this can be opted out of per below), and it throws on malformed attribute shapes (empty policy strings, whitespace-only roles) so typos are caught before traffic arrives.
4. When `ITrainExecutionService.QueueAsync()` or `RunAsync()` runs, it invokes the registered `ITrainAuthorizationService` before reading the input JSON. Every caller-built enqueue goes through `QueueAsync`, including the operations surface and the dashboard (which enqueues inside a trusted scope); see [The Operations Surface](#the-operations-surface).
5. The default implementation (`TrainAuthorizationService` from `Trax.Api`) is fail-closed. It grabs the current user from `IHttpContextAccessor` and evaluates each requirement:
   - **Policy**: calls `IAuthorizationService.AuthorizeAsync(user, policyName)`.
   - **Roles**: compares the upper-invariant `ClaimTypes.Role` claims against the normalized required set.
6. If any check fails, `TrainAuthorizationException` is thrown. Its public `Message` is always the generic string `"Not authorized."`; the train name, failing policy, and required roles live only on the exception's `TrainName` and `Reason` properties for server-side logging.
7. GraphQL surfaces the error with code `TRAX_AUTHORIZATION` and the same generic message. The train name, policy name, and role names never cross the wire.

### Fail-Closed Behavior

When an `HttpContext` is absent and no trusted execution scope is active, the service denies. This protects against accidental invocation from background services, tests, or custom middleware that bypasses the normal request pipeline. Scheduler and remote-worker paths explicitly mark themselves as trusted via `ITrustedExecutionScope.BeginTrusted(...)` so pre-authorized queued work still runs.

### Opting Out for Scheduler-Only Hosts

A process that registers `[TraxAuthorize]` trains for discovery but never serves API submissions (e.g. a standalone scheduler worker) can opt out of the fail-closed startup check:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connStr))
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(Program).Assembly)
        .AllowMissingAuthorizationService()));
```

Only use this from processes that genuinely never accept API submissions. The flag disables the fail-closed guard; every `[TraxAuthorize]` gated train will run without authorization checks in that process.

## Train Discovery Shows Auth Requirements

The GraphQL `trains` query includes authorization metadata in the response. Consumers can use this to build UIs that show which trains are available and what access they require.

```json
{
  "serviceTypeName": "IDeleteUserTrain",
  "implementationTypeName": "DeleteUserTrain",
  "inputTypeName": "DeleteUserInput",
  "outputTypeName": "Unit",
  "lifetime": "Transient",
  "inputSchema": [ ... ],
  "requiredPolicies": ["Admin"],
  "requiredRoles": []
}
```

Trains without `[TraxAuthorize]` return empty arrays for both fields.

## Combining with Endpoint-Level Auth

Per-train auth and endpoint-level auth are complementary. A typical setup might look like:

```csharp
// All Trax endpoints require authentication
app.UseTraxGraphQL(configure: endpoint => endpoint
    .RequireAuthorization());

// Individual trains require specific policies
[TraxAuthorize("Admin")]
[TraxMutation]
public class AdminOnlyTrain : ServiceTrain<AdminInput, Unit>, IAdminOnlyTrain { ... }
```

The endpoint-level check runs first (before the request reaches the handler). The per-train check runs inside the handler, after the train is resolved by name.

## The Scheduler and Remote Workers Are Trusted

Authorization is enforced once, at API submission time. When a train is queued or run through GraphQL, the request carries an `HttpContext` and the authorization check evaluates against the caller's identity. Work that makes it past that check is written to the queue as already-authorized.

Later execution paths are trusted:

- The scheduler dequeues from `work_queue` and calls `ITrainBus.RunAsync()` directly. It never reaches `ITrainAuthorizationService`.
- Remote workers pull queued work over HTTP and execute it via `ITrainExecutionService.RunAsync()`. Because there is no `HttpContext` on the worker side, the authorization check treats the caller as trusted infrastructure and skips.

This means you can safely decorate a train with `[TraxAuthorize("Admin")]` and still schedule it via `AddScheduler()`, run it from a remote worker, or both. The authorization gate is the API boundary.

## The Operations Surface

The admin and operations surface (the GraphQL `operations` namespace and the dashboard) enqueues work in two ways, and they are authorized differently. `Trax.Docs/adr/0017` records the decision.

| Action | What decides the train and input | Authorized by |
|---|---|---|
| `queueTrain`, `requeueExecution` | The caller | The train's own `[TraxAuthorize]` requirements, through `ITrainExecutionService.QueueAsync`, **plus** the operations gate |
| The dashboard's queue dialog and **Re-queue** button | The dashboard user | The dashboard host's own gate. They go through `QueueAsync` inside a trusted scope (`"dashboard"`), so per-train `[TraxAuthorize]` does not apply; `OnQueue`, the subject key and the input cap do |
| `triggerManifest`, `triggerManifestDelayed`, `triggerGroup`, re-queueing dead letters | A manifest | The operations gate only (`GateOperations`, `RequireAuthorization`, or `AllowAnonymousOperations`), not per-train requirements |
| The dashboard's **Run** dialog | The dashboard user | The dashboard host's own gate. It submits directly to the job submitter |
| The ManifestManager, and dormant dependents a parent train activates | The system (a dormant dependent's input is chosen at runtime by the parent train's code) | Nothing further: the work runs inside a scheduled or already-authorized train. These entries skip `QueueSubjectKey` and `OnQueue` |

A caller-built enqueue is authorized **before** its input JSON is read, so a caller who may not run the train gets `TRAX_AUTHORIZATION` even when the input is malformed, and learns nothing about the input the train expects.

Triggering a manifest re-runs work whose train and input were fixed when the manifest was defined, so it is treated as operating the scheduler rather than as submitting new work. That puts the whole trigger and dead-letter surface behind a single admin gate. Protect it accordingly: an operator who passes the gate can trigger any manifest. The admin area is expected to gain its own user-provided authorization (for example Microsoft Entra) later.

The dashboard is the admin surface, gated as a whole by the host that serves it. A Blazor Server circuit has no HTTP request behind a click, so the enforcer, which reads the user from the request, could not check a user there anyway. Protect the dashboard route the way you would protect any admin UI. The mediator's runtime fail-closed check for a `[TraxAuthorize]` train with no `ITrainAuthorizationService` registered honours a trusted scope too, but that only matters where hosted services do not run, such as the Lambda runner or a bare `ServiceProvider`. In a hosted app, `AuthorizationRegistrationValidator` refuses to start a host that has `[TraxAuthorize]` trains and no `ITrainAuthorizationService`, trusted scope or not, so a dashboard-only or scheduler-only host must register an enforcer or call [`AllowMissingAuthorizationService()`](#opting-out-for-scheduler-only-hosts).

## Custom Authorization Logic

If policies and roles aren't enough, you can replace the default `ITrainAuthorizationService` with your own implementation:

```csharp
public class CustomTrainAuthorizationService : ITrainAuthorizationService
{
    public async Task AuthorizeAsync(
        TrainRegistration registration,
        CancellationToken ct = default)
    {
        // Your custom logic here: check a database, call an external service, etc.
        // Throw to deny, return normally to allow.
    }
}

// Register before AddTraxGraphQL (which calls AddTraxApi internally)
builder.Services.AddScoped<ITrainAuthorizationService, CustomTrainAuthorizationService>();
```

The interface is defined in `Trax.Mediator`, so your implementation doesn't need to reference `Trax.Api`.

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [UseTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [ITrainDiscoveryService](/docs/sdk-reference/mediator-api/train-discovery) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/train-execution) | [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [TraxQueryModel](/docs/sdk-reference/graphql-api/query-models)
