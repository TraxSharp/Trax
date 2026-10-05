---
layout: default
title: Authorization
description: "How Trax gates its GraphQL API: endpoint policies, TraxAuthorize on trains and query models, TraxAllowAnonymous, combinator semantics and startup validation."
nav_order: 9
section: Guides
---

> **Security notice.** NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

# Authorization

Trax supports four levels of authorization for the API layer:

- **Endpoint-level**: gate every GraphQL operation behind one policy with `RequireAuthorization()` on the GraphQL builder (`AddTraxGraphQL(graphql => graphql.RequireAuthorization())`). See [Combining with Endpoint-Level Auth](#combining-with-endpoint-level-auth).
- **The operations namespace**: gate only the built-in `operations` queries and mutations with `GateOperations(policy, roles)`, leaving the rest of the endpoint open. See [The Operations Surface](#the-operations-surface).
- **Per-train**: restrict individual trains using the `[TraxAuthorize]` attribute on the train class. When a request comes in to run or queue a train, Trax checks the attribute against the current HTTP user before executing anything.
- **Per-model**: restrict `[TraxQueryModel]`-exposed entities using the same `[TraxAuthorize]` attribute on the entity class. The directive is enforced at GraphQL type level, so the gate also applies when the entity is reached through a navigation property on an ungated parent, whether the request selects it or filters or sorts through it.

A surface exposed via GraphQL can also be explicitly opened to anonymous access with `[TraxAllowAnonymous]`. See [Anonymous Access via TraxAllowAnonymous](#anonymous-access-via-traxallowanonymous) below.

The [Auth sample](/docs/samples/auth) uses every one of these in one host, with API keys and JWTs side by side, and its E2E suite proves each gate.

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
| A namespace Trax reaches from the root through an ungated field (`DiscoverQueries`, `DispatchMutations`, and the per-namespace types under them) | **Yes.** It is as reachable as the root. |
| A type under the `operations` namespace, gated by `GateOperations(...)` or `RequireAuthorization()` | No. It inherits the namespace's gate. |
| A type under the `operations` namespace, under `AllowAnonymousOperations()` | **Yes.** The namespace has no gate of its own, so it is as reachable as the root. |
| A query model's navigation target | As for a query model: no when it carries `[TraxAuthorize]`, **yes** when it carries `[TraxAllowAnonymous]`. |
| Neither marker | No. The type reaches the schema only inside a train's output, whose posture governs. A query model's navigation target must declare its own posture (see [Entities a Query Model Reaches](#entities-a-query-model-reaches)). |

`[TraxAuthorize]` and `[TraxAllowAnonymous]` both apply to a method, so a resolver declares its posture directly and Trax emits the matching `@authorize` directive for it. The combinator semantics are the same as on a train or an entity (see [How Policies and Roles Combine](#how-policies-and-roles-combine)): each attribute is a requirement of its own, so a method attribute under a class-level one adds to it. `[TraxAuthorize(Roles = "Support")]` on a method of a class carrying `[TraxAuthorize(Roles = "Admin")]` requires both roles.

```csharp
using HotChocolate;
using HotChocolate.Types;
using Trax.Effect.Attributes;

[ExtendObjectType(typeof(Article))]     // Article is [TraxAllowAnonymous]
public sealed class ArticleExtensions
{
    // Inherits nothing from Article, so it says what it is.
    [TraxAuthorize(Roles = "subscriber")]
    public async Task<IReadOnlyList<Excerpt>> GetExcerpts([Parent] Article article) => ...;
}
```

On the extension **class**, `[TraxAuthorize]` applies to every field that extension contributes, including a field HotChocolate builds from a property of the class, which cannot carry the attribute itself. It does not touch the type being extended, so gating an extension of a `[TraxAllowAnonymous]` entity does not re-gate the entity.

A subscription is the same construct: `[ExtendObjectType("LifecycleSubscriptions")]` adds a field to a root type, so every `[Subscribe]` field has to declare.

The check walks the merged schema at host start, so it sees a type extension however it was registered: through `AddTypeExtension(s)`, from a type module added with `AddTypeModule<T>()`, or from a `ConfigureSchema` callback. A field built from a lambda, in such a callback or in a type module, has no member to carry an attribute and is skipped; it is gated by the code that builds it.

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
| `Roles` | `string?` | Comma-separated list of roles. The user must have at least one. Role comparison is exact and case-sensitive. |

The attribute works on classes, interfaces, and base classes. Trax collects the attributes it finds across the implementation type's interface chain and base chain, and every one of them applies, so `[TraxAuthorize("Admin")]` on an `IMyTrain` interface is honored even when the implementing class carries no attribute. Decorator-wrapped trains inherit their authorization requirements through the same mechanism.

Role comparison is exact and case-sensitive, the way `@authorize` on a query model and ASP.NET Core's `RequireRole` compare roles (`ClaimsPrincipal.IsInRole`, ordinal against each identity's role claim type): `[TraxAuthorize(Roles = "Admin")]` does not match a principal carrying `ClaimTypes.Role = "admin"`. Train subscriptions compare the same way. Declare roles in the casing your identity provider issues them. Before this, `TrainAuthorizationService` upper-cased both sides, so `admin` matched `Admin`, and a culture case mapping could equate characters a reader sees as different (see `Trax.Docs/adr/0026`).

## How Policies and Roles Combine

Multiple attributes and mixed policy/role specifications combine as follows:

| Attribute form | What the user must satisfy |
|----------------|---------------------------|
| `[TraxAuthorize]` (bare) | Be authenticated. No additional policy or role requirement. |
| `[TraxAuthorize("P")]` | Policy `P` must pass. |
| `[TraxAuthorize(Roles = "A,B")]` | Hold role `A` OR role `B`. |
| `[TraxAuthorize("P", Roles = "A")]` | Policy `P` must pass AND hold role `A`. |
| `[TraxAuthorize("P1")] [TraxAuthorize("P2")]` | Both `P1` and `P2` must pass. Policies AND across attributes. |
| `[TraxAuthorize(Roles = "A")] [TraxAuthorize(Roles = "B")]` | Hold role `A` AND role `B`. Each attribute is a requirement of its own. |
| `[TraxAuthorize("P")] [TraxAuthorize(Roles = "A")]` | Policy `P` must pass AND hold role `A`. |

In short: each attribute is a requirement, and every requirement has to pass. Within one attribute, a comma-separated `Roles` list is any of; across attributes, everything is all of. This is how ASP.NET Core combines separate `[Authorize]` attributes, and it holds wherever the attributes come from: the train class and its interface or base class, an entity and its base, an extension class and its method. Adding an attribute can therefore only narrow who gets in. To accept either of two roles, list them in one attribute: `[TraxAuthorize(Roles = "A,B")]`. A bare attribute requires nothing beyond authentication.

> **Changed.** Before this, role lists from separate attributes were merged into one list, and holding any role from it was enough. `[TraxAuthorize(Roles = "A")] [TraxAuthorize(Roles = "B")]` meant A or B, and a method-level role added to a class-level one widened access. Write `Roles = "A,B"` in one attribute for the old meaning (see `Trax.Api/docs/adr/0032`).

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
- **Filtering and sorting through a navigation**: a `where` or `order` on an ungated parent that passes through a navigation to the entity (`publicOwners(where: { privateBooks: { some: { title: { eq: "..." } } } })`) is authorized against the entity's directives before the query runs, exactly as selecting it would be. A caller who may read the entity filters and sorts through it as usual; anyone else gets `TRAX_AUTHORIZATION` for the whole field. HotChocolate's `@authorize` cannot sit on an input field, so Trax evaluates the target type's own directives through HotChocolate's authorization handler. This applies on every field that takes a `where` or `order` argument, whoever contributed it: the model's entry field, a type extension's `[UseFiltering]` or `[UseSorting]` resolver, and a `[UseFiltering]` collection navigation on an entity.

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
| `[TraxAuthorize(Roles = "A")] [TraxAuthorize(Roles = "B")]` | Hold role `A` AND role `B`. Each attribute is a requirement of its own. |
| `[TraxAuthorize("P")] [TraxAuthorize(Roles = "A")]` | Policy `P` must pass AND hold role `A`. |

### Startup Validation

`QueryModelAuthorizationValidator` runs as a hosted service at host start. It throws if any entity, a query model or a gated entity a query model reaches, references an authorization policy that has not been registered via `services.AddAuthorization(...)`. This catches typoed policy names (`"AdmnPolicy"`) before the first request rather than turning the gate into a silent deny-all in production. Roles are not validated against the principal store (Trax has no view of what roles can exist); attribute shape (empty or whitespace-only `Policy`, all-empty `Roles` CSV) is validated at `TraxGraphQLBuilder.Build` time.

### Which Scheme Authenticates a GraphQL Request

`[TraxAuthorize]` on a train and the `@authorize` directive on a query model both read `HttpContext.User`. ASP.NET Core's `UseAuthentication()` middleware fills it only from the **default authentication scheme**, and no Trax `AddTrax*Auth` call sets one, so on a host with API keys and JWTs side by side it would otherwise stay anonymous.

Trax handles this automatically. On every host, `AddTraxGraphQL` wires HotChocolate's authorization and a HotChocolate `IHttpRequestInterceptor` (`TraxHttpAuthenticationInterceptor`) that runs before each GraphQL HTTP request. The interceptor:

1. With an endpoint policy (`RequireAuthorization(...)` on the builder), it authenticates the request with that policy's schemes, as ASP.NET Core does for an endpoint gated by the same policy.
2. Otherwise, if the request is already authenticated (a default scheme, or other middleware), it leaves it alone.
3. Otherwise it tries every registered authentication scheme **in registration order** and keeps the first principal that authenticates. A request carrying both an API key and a bearer token is therefore authenticated by whichever scheme was registered first.
4. If no scheme authenticates the request, the caller stays anonymous, and every gate refuses it with `TRAX_AUTHORIZATION`. A credential a scheme rejects (an unknown key, an expired or forged token) is the same as no credential.

The interceptor runs only for GraphQL HTTP execution requests, so the Nitro IDE page and WebSocket subscription upgrades are not affected. Subscriptions authenticate separately, from the `connection_init` payload, through `TraxCompositeSocketInterceptor` and the per-scheme strategies it delegates to (`TraxApiKeySocketInterceptor`, `TraxJwtSocketInterceptor`, `TraxJwtDispatcherSocketInterceptor`).

No consumer configuration is required.

### Limitations

- **A property cannot be gated on its own.** `[TraxAuthorize]` does not apply to a property. A resolver *method* on the entity can carry it, and that field is gated on every host, whether or not the host registers a type extension. The same holds for a method on a train's output type. If `User.email` must be admin-only but `User.displayName` must be public, expose the email through a gated method, use a custom train, or split the entity into two types via `ExposeAs`.
- **Row-level filtering is not supported.** `[TraxAuthorize]` answers "can this user read *this type*", not "which rows of this type." For per-row scoping (tenancy, ownership, subscription tier), use EF Core global query filters that read the current caller from a scoped service, such as one built over [`TraxCaller`](/docs/sdk-reference/api-auth/trax-caller); [Owner-scoped contexts](/docs/effect/effect-providers/domain-data-contexts#owner-scoped-contexts) has the registration, and the [Bookworm sample](/docs/samples/bookworm) a working one. Key ownership on the `trax:principal-id` claim as it is, `{scheme}:{id}`: the scheme is part of the id, so one issuer's `sub` cannot match another's. See [Qualified Principal Ids](/docs/migration-guides/qualified-principal-ids). Nothing in Trax's authorization sees those filters, so an entity with a correct `[TraxAuthorize]` and a missing filter serves every user's rows to any authenticated caller. The [owner-scope census](/docs/reference/architecture-guards#the-owner-scope-census) in `Trax.Effect.Data.Testing` is the check that does see them.

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

An anonymous query for `discover.public.announcements { title }` succeeds. The same query selecting `relatedMatch { matchId }` fires the gated child's directive and returns `TRAX_AUTHORIZATION`, and so does `announcements(where: { relatedMatch: { matchId: { eq: "..." } } })` or an `order` by `relatedMatch.matchId`. The anonymous parent does not propagate openness to its gated children.

### Entities a Query Model Reaches

HotChocolate builds an object type, a filter input and a sort input for every navigation a query model exposes, including a navigation to an entity that is not a `[TraxQueryModel]`. A public method of the entity that returns another entity (`GetOwner()`, bound as `owner`) reaches it the same way. That entity is part of the schema, so it states its posture the same way a model does: `[TraxAuthorize]` or `[TraxAllowAnonymous]` on its own class.

```csharp
[TraxQueryModel(Namespace = "public")]
[TraxAllowAnonymous]
public class Post
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public Account? Author { get; set; }   // not a query model
}

[TraxAuthorize(Roles = "Admin")]           // required: Post reaches it
public class Account
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
}
```

On an endpoint not gated as a whole with `RequireAuthorization()`, the host refuses to start when an entity a query model reaches declares neither marker, behind a gated model too. The message names the entity and every schema coordinate that reaches it:

```
Entity 'MyApp.Account' is not a [TraxQueryModel], but a query model reaches it through
'Post.author', 'PostFilterInput.author', 'PostSortInput.author', ...
```

There are three ways to answer: a marker on the entity class, leaving the navigation out of the model's exposed field set (`BindFields = Explicit` or `ExposeAs`, which narrow the filter and sort inputs too), or gating the whole endpoint. A `[TraxAuthorize]` on the entity gates its object type wherever it appears, and its filter and sort inputs as described above. A class EF Core maps as owned is part of the entity that holds it and needs no marker of its own. The decision is recorded in `Trax.Api/docs/adr/0025`.

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

Trax evaluates these policies at runtime using ASP.NET Core's `IAuthorizationService`. Register every policy before you name it:

| A policy that is not registered, named by | Fails |
|---|---|
| A `[TraxQueryModel]` entity's `[TraxAuthorize]` | at startup, naming the entity and the policy |
| The builder's `RequireAuthorization(policy)` | at startup, naming the policy |
| A train's `[TraxAuthorize]` | at request time: every call to the train fails with HotChocolate's masked `"Unexpected Execution Error"`, not `TRAX_AUTHORIZATION`, and the train never runs |
| `GateOperations(policy: ...)` | at request time: every `operations` call fails |

Roles need no registration: a role is matched against the caller's role claims as it is written.

## How It Works

1. `ITrainDiscoveryService` reads `[TraxAuthorize]` and `[TraxAllowAnonymous]` attributes across the implementation, its base chain, and every implemented interface. Roles are kept as declared (before Trax.Mediator 1.23.0 they were upper-cased); roles and policies are deduplicated. The requirements (and a `HasAllowAnonymousAttribute` flag) are stored on each `TrainRegistration`.
2. `AddTraxGraphQL` enforces the [Required Exposure Posture](#required-exposure-posture) for every exposed train, and `TraxGraphQLBuilder.Build()` does the same for every `[TraxQueryModel]` entity. Both share one rule: a surface with neither marker (on an open endpoint), both markers, or `[TraxAllowAnonymous]` under `RequireAuthorization()` fails startup with a message naming the offending types.
3. At host start, the mediator's internal `AuthorizationRegistrationValidator` runs as a hosted service. It throws if any train carries `[TraxAuthorize]` but no `ITrainAuthorizationService` is registered (this can be opted out of per below), and it throws on malformed attribute shapes (empty policy strings, whitespace-only roles) so typos are caught before traffic arrives.
4. When `ITrainExecutionService.QueueAsync()` or `RunAsync()` runs, it invokes the registered `ITrainAuthorizationService` before reading the input JSON. Every caller-built enqueue goes through `QueueAsync`, including the operations surface and the dashboard (which enqueues inside a trusted scope); see [The Operations Surface](#the-operations-surface).
5. The default implementation (`TrainAuthorizationService` from `Trax.Api`) is fail-closed. It grabs the current user from `IHttpContextAccessor` and evaluates each requirement:
   - **Policy**: calls `IAuthorizationService.AuthorizeAsync(user, policyName)`.
   - **Roles**: each attribute that names roles passes when `user.IsInRole(role)` holds for at least one of its roles, an exact, case-sensitive match against the caller's role claims, and every such attribute has to pass.
6. If any check fails, `TrainAuthorizationException` is thrown. Its public `Message` is always the generic string `"Not authorized."`; the train name, failing policy, and required roles live only on the exception's `TrainName` and `Reason` properties for server-side logging.
7. GraphQL surfaces the error with code `TRAX_AUTHORIZATION` and the same generic message. The train name, policy name, and role names never cross the wire.

### Fail-Closed Behavior

When an `HttpContext` is absent and no trusted execution scope is active, the service denies. This protects against accidental invocation from background services, tests, or custom middleware that bypasses the normal request pipeline. Scheduler and remote-worker paths explicitly mark themselves as trusted via `ITrustedExecutionScope.BeginTrusted(...)` so pre-authorized queued work still runs. A trusted scope lasts exactly as long as its handle: scopes nest, and disposing one while an inner scope is still open closes it without ending the inner one. When the inner one is disposed, the flow returns to the nearest scope still open, or to untrusted, never to a scope already disposed.

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
// Every operation on the endpoint requires an authenticated caller
builder.Services.AddTraxGraphQL(graphql => graphql.RequireAuthorization());

// Individual trains require specific policies
[TraxAuthorize("Admin")]
[TraxMutation]
public class AdminOnlyTrain : ServiceTrain<AdminInput, Unit>, IAdminOnlyTrain { ... }
```

The endpoint-level check runs first. The per-train check runs inside the handler, after the train is resolved by name.

Gate the endpoint through the builder's `RequireAuthorization()`, not only through an ASP.NET convention on the mapped route (`app.UseTraxGraphQL(configure: endpoint => endpoint.RequireAuthorization())`). The builder's gate is the one Trax can see: the startup checks for exposed surfaces (the `operations` namespace, `[TraxAllowAnonymous]` contradictions) count it, and they do not count a route convention. A route convention still works as an additional layer, enforced by ASP.NET before HotChocolate runs.

## The Scheduler and Remote Workers Are Trusted

Authorization is enforced once, at API submission time. When a train is queued or run through GraphQL, the request carries an `HttpContext` and the authorization check evaluates against the caller's identity. Work that makes it past that check is written to the queue as already-authorized.

Later execution paths are trusted:

- The scheduler dequeues from `work_queue` and calls `ITrainBus.RunAsync()` directly. It never reaches `ITrainAuthorizationService`.
- Remote workers receive work over HTTP. A queued job runs through the job runner and `ITrainBus`, like the scheduler's own. A remote run (`UseRemoteRun`) goes through `ITrainExecutionService.RunAsync()` inside a trusted execution scope (`ITrustedExecutionScope.BeginTrusted("scheduler.remote-run")`), so the authorization check skips. A missing `HttpContext` on its own is not trust: outside a trusted scope, the default `TrainAuthorizationService` refuses a `[TraxAuthorize]` train with "No request context and no trusted execution scope." (see [Fail-Closed Behavior](#fail-closed-behavior)). A custom `ITrainAuthorizationService` should make the same distinction, keying on `ITrustedExecutionScope.IsTrusted` rather than on whether a request is present.

This means you can safely decorate a train with `[TraxAuthorize("Admin")]` and still schedule it via `AddScheduler()`, run it from a remote worker, or both. The authorization gate is the API boundary.

Because a remote worker trusts what it is sent, the worker's own endpoints are where that trust is guarded. Every runner entry point refuses to start without an [authorization posture](/docs/scheduler/remote-execution#authorization-posture): a signing key shared with the scheduler, an authorization policy that admits only the scheduler, or an explicit, logged `AllowUnsignedRequests()`. Inside a remote run, `TraxCaller.IsTrusted` is true, so anything keyed on it (row-level filters included) treats the request as the scheduler's.

## The Operations Surface

The admin and operations surface (the GraphQL `operations` namespace and the dashboard) enqueues work in two ways, and they are authorized differently. `Trax.Docs/adr/0017` records the decision.

| Action | What decides the train and input | Authorized by |
|---|---|---|
| `queueTrain`, `requeueExecution` | The caller | The train's own `[TraxAuthorize]` requirements, through `ITrainExecutionService.QueueAsync`, **plus** the operations gate |
| The dashboard's queue dialog and **Re-queue** button | The dashboard user | The dashboard host's own gate. They go through `IOperationsService.QueueTrainAsync`, and so `QueueAsync`, inside a trusted scope (`"dashboard"`), so per-train `[TraxAuthorize]` does not apply; `OnQueue`, the subject key and the input cap do |
| `triggerManifest`, `triggerManifestDelayed`, `triggerGroup`, re-queueing dead letters | A manifest | The operations gate only (`GateOperations`, `RequireAuthorization`, or `AllowAnonymousOperations`), not per-train requirements |
| The dashboard's **Run** dialog | The dashboard user | The dashboard host's own gate. It calls `IOperationsService.RunTrainAsync` inside the dashboard's trusted scope, the call behind `runTrain`, which runs the train's `OnQueue` hook, applies the input cap and submits to the job submitter the train is routed to. `QueueSubjectKey` does not apply, so the run is not serialized against other work for its subject (`Trax.Docs/adr/0019`); outside a trusted scope a subject-keyed train is refused and must be queued |
| The ManifestManager, and dormant dependents a parent train activates | The system (a dormant dependent's input is chosen at runtime by the parent train's code) | Nothing further: the work runs inside a scheduled or already-authorized train. These entries skip `QueueSubjectKey` and `OnQueue` |

A caller-built enqueue is authorized **before** its input JSON is read, so a caller who may not run the train gets `TRAX_AUTHORIZATION` even when the input is malformed, and learns nothing about the input the train expects.

**`[TraxAuthorize]` gates the train, not the record.** A caller who passes it supplies the input, and the input is what [`QueueSubjectKey`](/docs/core/trains-and-junctions#queuesubjectkey-serializing-work-that-touches-the-same-thing) turns into a subject key, so the caller chooses which subject their entry serializes against. The queue priority is a caller-supplied argument too, so they also choose where it sits in that subject's order. Subject keys are one global space, not one per train. Where that matters, authorize the record itself in `OnQueue`, which runs with the caller's identity available, or scope the key per tenant so a key a caller can name is always one they own.

Triggering a manifest re-runs work whose train and input were fixed when the manifest was defined, so it is treated as operating the scheduler rather than as submitting new work. That puts the whole trigger and dead-letter surface behind a single admin gate. Protect it accordingly: an operator who passes the gate can trigger any manifest. The admin area is expected to gain its own user-provided authorization (for example Microsoft Entra) later.

The dashboard is the admin surface, gated as a whole by the host that serves it. A Blazor Server circuit has no HTTP request behind a click, so the enforcer, which reads the user from the request, could not check a user there anyway. Protect the dashboard route the way you would protect any admin UI. The mediator's runtime fail-closed check for a `[TraxAuthorize]` train with no `ITrainAuthorizationService` registered honours a trusted scope too, but that only matters where hosted services do not run, such as the Lambda runner or a bare `ServiceProvider`. In a hosted app, the mediator's startup check refuses to start a host that has `[TraxAuthorize]` trains and no `ITrainAuthorizationService`, trusted scope or not, so a dashboard-only or scheduler-only host must register an enforcer or call [`AllowMissingAuthorizationService()`](#opting-out-for-scheduler-only-hosts).

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

// Register after AddTraxGraphQL: it calls AddTraxApi, which adds the default enforcer,
// and the container resolves the last registration
builder.Services.AddTraxGraphQL();
builder.Services.AddScoped<ITrainAuthorizationService, CustomTrainAuthorizationService>();
```

The interface is defined in `Trax.Mediator`, so your implementation doesn't need to reference `Trax.Api`.

## SDK Reference

> [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxAllowAnonymous](/docs/sdk-reference/attributes/trax-allow-anonymous) | [ITrustedExecutionScope](/docs/sdk-reference/mediator-api/i-trusted-execution-scope) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [UseTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [ITrainDiscoveryService](/docs/sdk-reference/mediator-api/train-discovery) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/train-execution) | [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [TraxQueryModel](/docs/sdk-reference/graphql-api/query-models)
