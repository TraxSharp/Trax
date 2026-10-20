---
title: Startup refusals
description: "Every security posture a Trax host refuses to start with: the cause, when the refusal is raised, how its message begins, and what answers it."
type: reference
area: security
nav_order: 4
source:
  - Trax.Api/src/Trax.Api.GraphQL/Startup
  - Trax.Api/src/Trax.Api.GraphQL/Configuration/ExposureAuthorizationRule.cs
  - Trax.Api/src/Trax.Api.GraphQL/Configuration/TypeExtensionExposureRule.cs
  - Trax.Api/src/Trax.Api.GraphQL/Configuration/TraxGraphQLBuilder/TraxGraphQLBuilder.Build.cs
  - Trax.Api/src/Trax.Api.GraphQL/Configuration/TraxGraphQLBuilder/TraxGraphQLBuilder.Operations.cs
  - Trax.Api/src/Trax.Api.GraphQL/Authorization/TraxGraphQLAuthPolicyValidator.cs
  - Trax.Api/src/Trax.Api.Auth.ApiKey/DemoApiKeyEnvironmentValidator.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt/DemoJwtKeyEnvironmentValidator.cs
  - Trax.Mediator/src/Trax.Mediator/Services/TrainAuthorization/AuthorizationRegistrationValidator.cs
  - Trax.Api/src/Trax.Api.Dashboard/Extensions/ApiDashboardEndpointExtensions.cs
  - Trax.Api/src/Trax.Api.Dashboard/Configuration/TraxApiDashboardOptions.cs
---

# Startup refusals

A Trax host whose security posture is missing or contradictory does not start. Each refusal is an
`InvalidOperationException` whose message names the offending type, field, policy or call and the
change that answers it. Nothing on this page is a warning: the host stops.

A refusal is raised at one of these points:

| Point | What runs there |
|---|---|
| The builder call | The method itself throws while `Program.cs` registers services, before `builder.Build()`. |
| `AddTraxGraphQL` | The GraphQL builder's `Build()` and the train census, while `AddTraxGraphQL(...)` runs. |
| Host start | A hosted service checks the built container and schema in `StartingAsync`, before the host serves a request. |
| `UseTraxApiDashboard` | The operations console checks its posture while it is mapped, and once more as the endpoint is built at host start. |

## Exposed surfaces

A surface is exposed when it is a `[TraxQuery]` or `[TraxMutation]` train, a `[TraxBroadcast]`
train, a `[TraxQueryModel]` entity, an entity a query model reaches, or a field a type extension
adds. The rule behind these refusals is [Required exposure posture](/docs/security/authorization-rules#required-exposure-posture).

| Cause | Raised | Message begins | Answer |
|---|---|---|---|
| An exposed train with neither `[TraxAuthorize]` nor `[TraxAllowAnonymous]`, on an endpoint without `RequireAuthorization()` | `AddTraxGraphQL` | `Trax GraphQL exposure authorization check failed:`, then one line per train: `GraphQL-exposed train '<type>' is exposed via GraphQL but declares neither ...` (`[TraxBroadcast] train` for a broadcast-only one) | Add one of the two markers, or gate the endpoint |
| A `[TraxQueryModel]` entity with neither marker, on an endpoint without `RequireAuthorization()` | `AddTraxGraphQL` | `[TraxQueryModel] entity '<type>' is exposed via GraphQL but declares neither ...` | Add one of the two markers, or gate the endpoint |
| Both markers on one train or entity, directly or through a base class or interface | `AddTraxGraphQL` | `... '<type>' declares both [TraxAllowAnonymous] and [TraxAuthorize] ...` | Remove one |
| `[TraxAllowAnonymous]` on an exposed train or entity while the builder calls `RequireAuthorization()` | `AddTraxGraphQL` | `... '<type>' declares [TraxAllowAnonymous], but the GraphQL endpoint is gated via RequireAuthorization(). ...` | Remove the attribute, or the endpoint gate |
| `[TraxAuthorize]` with an empty or whitespace `Policy`, or a `Roles` list that holds no role, on a query model | `AddTraxGraphQL` | `[TraxAuthorize] on '<type>' has an empty or whitespace Policy value.` or `[TraxAuthorize(Roles="...")] on '<type>' parsed to zero roles ...` | Name a policy or a role, or drop the argument |
| The same, on a train or one of its interfaces | Host start | As above | As above |
| An entity a query model reaches (a navigation, or a method returning an entity) that is not a query model and declares neither marker, on an endpoint without `RequireAuthorization()` | Host start | `<n> entit(y/ies) reached from a [TraxQueryModel] declare no usable authorization posture ...`, naming every schema coordinate that reaches it | A marker on the entity class, the navigation left out of the model's field set (`BindFields = Explicit` or `ExposeAs`), or an endpoint gate |
| A field a type extension adds to a parent that gives it no gate (a `[TraxAllowAnonymous]` type, a root type, a namespace reached from the root, the `operations` namespace under `AllowAnonymousOperations()`), with no marker of its own | Host start | `<n> GraphQL field(s) or type extension(s) cannot be served as declared:` | `[TraxAuthorize]` or `[TraxAllowAnonymous]` on the resolver method or the extension class. See [Fields added by a type extension](/docs/security/authorization-rules#fields-added-by-a-type-extension) |
| Both markers on such a field | Host start | As above | Remove one |
| HotChocolate's `[Authorize]` or `[AllowAnonymous]` on a field a type extension adds, or on the extension class | Host start | As above, then `GraphQL field '<Type.field>' (<resolver>) declares its authorization posture with [Authorize], which Trax does not read. ...` | `[TraxAuthorize]` or `[TraxAllowAnonymous]` |
| The `@authorize` directive Trax put on a gated query model, its entry field or a gated reached entity is missing from the built schema, or one is present on a `[TraxAllowAnonymous]` entity | Host start | `... ConfigureSchema callback has ...` | Remove the `ConfigureSchema` override that replaced the type or directive |

## Policies

| Cause | Raised | Message begins | Answer |
|---|---|---|---|
| `RequireAuthorization(policy)` names a policy that is not registered, or `RequireAuthorization()` is called and no `AddTrax*Auth` call contributed the combined Trax policy | Host start | `AddTraxGraphQL(graphql => graphql.RequireAuthorization(...)) was called, but the authorization policy '<name>' is not registered.` | Register the policy, or an auth scheme |
| A train's `[TraxAuthorize]` names a policy that is not registered | Host start | `<n> authorization polic(y is/ies are) named by a train but not registered:` | `AddAuthorization(o => o.AddPolicy("<name>", ...))`, or correct the name |
| A query model's `[TraxAuthorize]` names a policy that is not registered | Host start | `... references an authorization policy that is not registered.` | As above |
| `GateOperations(policy: ...)`, a type extension's `[TraxAuthorize]`, or any other `@authorize` directive in the built schema names a policy that is not registered | Host start | `... named in the GraphQL schema but not registered:` | As above |

Roles are never checked at startup: Trax has no list of the roles that can exist.

## The operations namespace

| Cause | Raised | Message begins | Answer |
|---|---|---|---|
| `ExposeOperationQueries()` or `ExposeOperationMutations()` with no gate: no `GateOperations(...)`, no `RequireAuthorization()`, no `AllowAnonymousOperations()` | `AddTraxGraphQL` | `ExposeOperationMutations() exposes scheduler-control mutations ... but the GraphQL endpoint is not gated ...` (`ExposeOperationQueries() exposes the scheduler control plane's read surface ...` when only the queries are exposed) | `GateOperations(policy, roles)`, `RequireAuthorization()`, or `AllowAnonymousOperations()` |
| `GateOperations()` with neither a policy nor roles | The builder call | `GateOperations() needs a policy or roles: ...` | Name a policy or roles, or call `GateOperationsToAuthenticatedUsers()` |
| `GateOperations(roles: ...)` with a list that names no role (`","`) | The builder call | `GateOperations(roles: "<list>") names no role: ...` | Name one or more roles |
| `GateOperations(...)` with the namespace not exposed | `AddTraxGraphQL` | `GateOperations() was called but the operations namespace is not exposed ...` | Expose it, or remove the call |
| `AllowAnonymousOperations()` with `RequireAuthorization()` | `AddTraxGraphQL` | `AllowAnonymousOperations() was called together with RequireAuthorization(). ...` | Remove `AllowAnonymousOperations()` |
| `AllowAnonymousOperations()` with `GateOperations(...)` | `AddTraxGraphQL` | `AllowAnonymousOperations() was called together with GateOperations(). ...` | Remove one |
| The namespace exposed without `IOperationsService` | Host start | `AddTraxGraphQL() exposes the operations surface (ExposeOperationQueries / ExposeOperationMutations), but IOperationsService is not registered, ...` | `AddScheduler(...)` on the same host |
| The namespace exposed without the mediator's `ITrainExecutionService` | Host start | `AddTraxGraphQL() exposes the operations surface, but ITrainExecutionService is not registered, ...` | `AddMediator(...)` |
| The mutations exposed without `ITraxScheduler` or without an `IJobSubmitter` | Host start | `AddTraxGraphQL() exposes the operations mutations (ExposeOperationMutations), but ITraxScheduler is not registered, ...`, or `... but no IJobSubmitter is registered, ...` | `AddScheduler(...)` |

## Train authorization

| Cause | Raised | Message begins | Answer |
|---|---|---|---|
| A registered train carries `[TraxAuthorize]` and no `ITrainAuthorizationService` is registered | Host start | `One or more registered trains declare [TraxAuthorize] but no ITrainAuthorizationService is registered in the DI container. ...`, ending with the trains | `AddTraxGraphQL()` or `AddTraxApi()` registers the default; a host that serves no submissions calls [`AllowMissingAuthorizationService()`](/docs/security/authorization-rules#opting-out-for-scheduler-only-hosts) |
| A junction takes `TraxPrincipal` in its constructor and nothing registers it (every `AddTrax*Auth` call was skipped) | Host start | `<n> of <m> registered trains cannot run:`, naming the junction and `Trax.Api.Auth.TraxPrincipal` | `services.AddTraxPrincipalAccessor()`. See [Injecting TraxPrincipal](/docs/security/inject-the-current-principal) |

## Credentials

| Cause | Raised | Message begins | Answer |
|---|---|---|---|
| An API key containing `do-not-use-in-production` registered with `keys.Add(...)`, outside Development | Host start | `AddTraxApiKeyAuth() registered a key containing 'do-not-use-in-production', which marks a published demo key, ...` | Register demo keys only inside `IsDevelopment()`; elsewhere `AddHashed(...)` or a resolver |
| A JWT bearer scheme whose signing key contains `do-not-use-in-production`, outside Development, however the key was set | Host start | `The JWT bearer scheme '<scheme>' validates tokens with a signing key containing 'do-not-use-in-production', ...` | Load the key from a secret store, or `UseAuthority(...)` |

A key fetched from an authority's JWKS, or produced by an `IssuerSigningKeyResolver`, does not exist
at startup and is not checked.

## Subscriptions

| Cause | Raised | Message begins | Answer |
|---|---|---|---|
| The socket interceptor for the Trax schema has been replaced by HotChocolate's default, which accepts every `connection_init` | Host start | `<scheme> is registered, but the subscription socket interceptor for the Trax schema is HotChocolate's default, which accepts every connection_init. ...` | Remove the replacement, or register your own interceptor type with `ConfigureSchema(b => b.AddSocketSessionInterceptor<T>())` |

## The operations console

| Cause | Raised | Message begins | Answer |
|---|---|---|---|
| No posture: no `RequirePolicy(...)`, `RequireRoles(...)` or `AllowAnonymousShell()` | `UseTraxApiDashboard` | `UseTraxApiDashboard() needs to know who may load the dashboard, ...` | Choose one |
| `AllowAnonymousShell()` together with `RequirePolicy(...)` or `RequireRoles(...)` | The options call | `AllowAnonymousShell() and RequirePolicy()/RequireRoles() contradict each other: ...` | Remove one |
| `AllowAnonymousShell()` together with `HostAuthenticates()` | The options call | `AllowAnonymousShell() and HostAuthenticates() contradict each other: ...` | Remove one |
| `RequirePolicy(name)` names a policy that is not registered | `UseTraxApiDashboard` | `The dashboard requires the authorization policy '<name>', which is not registered. ...` | Register the policy |
| The console is gated but no authentication scheme can challenge or forbid a refused caller | `UseTraxApiDashboard` | `The dashboard requires authorization, but the host has no authentication scheme ...`, or names the missing schemes | Register a scheme, or name it on the policy with `AddAuthenticationSchemes(...)` |
| `AllowAnonymous()` chained on the returned builder over a policy or roles posture | Host start | `The Trax API dashboard at <path> was given AllowAnonymous(), which removes its RequirePolicy()/RequireRoles() posture ...` | Remove `AllowAnonymous()`, or use `AllowAnonymousShell()` |

[UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) documents the options
these refer to.

## Not refused at startup

- A host with no `AddTrax*Auth` call at all and no endpoint gate. Every gated surface then refuses
  every caller with `TRAX_AUTHORIZATION`, and only `[TraxAllowAnonymous]` surfaces answer.
- A train that is not exposed (no `[TraxQuery]`, `[TraxMutation]` or `[TraxBroadcast]`) and carries
  no marker. It is not reachable over GraphQL.
- A request that meets an `@authorize` directive naming an unknown policy on a host that never ran
  its startup checks. The caller gets `TRAX_AUTHORIZATION` with the generic message, and the policy
  name goes to the log.

A remote worker's entry points refuse to start without an authorization posture of their own; see
[Remote execution](/docs/scheduler/remote-execution).

## See also

- [Authorization rules](/docs/security/authorization-rules): the rules these refusals enforce.
- [Where Trax checks authorization](/docs/security/where-authorization-is-checked): why the host
  refuses rather than serves open.
- [Secure a GraphQL host](/docs/security/secure-a-graphql-host): a host that passes every check.
