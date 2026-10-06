---
layout: default
title: API Security
description: "Securing a Trax GraphQL host: API-key, JWT bearer and OIDC authentication, subscription auth, per-train authorization, audit logging and generic error messages."
nav_order: 10
section: Guides
---

# API Security

<a id="no-warranty"></a>

> NO WARRANTY FOR SECURITY. Trax.Api.Auth and Trax.Api.GraphQL.Audit are provided AS-IS. Trax, its authors, and contributors are NOT LIABLE for any security breach, credential leak, data loss, or damage arising from systems built on top of these packages. Securing your deployment is the SOLE RESPONSIBILITY OF THE CONSUMER.

This page covers authentication, audit logging, and operational hygiene for Trax GraphQL hosts. Read the [security disclaimer](https://github.com/TraxSharp/Trax.Api/blob/main/SECURITY-DISCLAIMER.md) before shipping any of this to production.

Trax ships three pluggable authentication schemes, all feeding the same [`TraxPrincipal`](/docs/sdk-reference/api-auth/trax-principal) abstraction:

- `Trax.Api.Auth.ApiKey` - header-based API keys for service-to-service calls.
- `Trax.Api.Auth.Jwt` - JWT bearer tokens for SPA and machine-to-machine API clients.
- `Trax.Api.Auth.Oidc` - OpenID Connect code flow for interactive browser sign-in.

Multiple schemes can coexist in a single host. Every `AddTrax*Auth` call contributes its scheme to the combined `TraxAuthClaimTypes.TraxAuthPolicy`, so endpoints protected by that policy accept credentials from any registered scheme. None of them becomes the default scheme; Trax authenticates each GraphQL request against the registered schemes itself (see [Which Scheme Authenticates a GraphQL Request](/docs/authorization#which-scheme-authenticates-a-graphql-request)).

## A Secured Host, End to End

This is the shape of a host with public surfaces, gated trains, an operator-only operations namespace and an audit trail, accepting API keys and JWTs. It is the [Auth sample](/docs/samples/auth)'s `Program.cs`, trimmed; the sample's E2E suite proves every gate in it.

Packages: `Trax.Effect.Data.Postgres`, `Trax.Effect.Provider.Json`, `Trax.Mediator`, `Trax.Scheduler`, `Trax.Api`, `Trax.Api.Auth.ApiKey`, `Trax.Api.Auth.Jwt`, `Trax.Api.GraphQL`, `Trax.Api.GraphQL.Audit`.

```csharp
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Audit;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("TraxDatabase")!;

// 1. Authentication. Demo credentials only in Development; real ones from configuration.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddTraxApiKeyAuth(keys => keys
        .Add("ops-key-do-not-use-in-production", id: "ops", "Operator")
        .Add("editor-key-do-not-use-in-production", id: "editor", "Editor"));
    builder.Services.AddTraxJwtAuth(jwt => jwt.UseSymmetricKey(
        "my-app-dev", "my-app", "my-app-dev-signing-key-do-not-use-in-production"u8.ToArray()));
}
else
{
    var ops = builder.Configuration.GetSection("Auth:OpsKey");
    if (ops.Exists())
        builder.Services.AddTraxApiKeyAuth(keys => keys.AddHashed(
            Convert.FromBase64String(ops["Salt"]!), Convert.FromBase64String(ops["Hash"]!),
            id: "ops", "Operator"));
    if (builder.Configuration["Auth:Jwt:Authority"] is { Length: > 0 } authority)
        builder.Services.AddTraxJwtAuth(authority, builder.Configuration["Auth:Jwt:Audience"]!);
}
builder.Services.AddAuthentication();       // the calls above are conditional
builder.Services.AddTraxPrincipalAccessor(); // so is the TraxPrincipal they register

// 2. Policies any [TraxAuthorize("...")] names. Roles need no registration.
builder.Services.AddAuthorization(o =>
    o.AddPolicy("VerifiedEmail", p => p.RequireClaim("email_verified", "true")));

// 3. Trax. AddScheduler backs the operations namespace.
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString).AddJson())
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler));

// 4. GraphQL: operators only on `operations`, everything else per surface, every request audited.
builder.Services.AddTraxGraphQL(graphql => graphql
    .ExposeOperationQueries()
    .ExposeOperationMutations()
    .GateOperations(roles: "Operator")
    .AddAudit<MyAuditSink>());

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.Run();
```

The trains and entities carry the rest of the posture. A queue-only mutation for one role, which its
caller can watch over a subscription, and a table any signed-in caller can read:

```csharp
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

[TraxAuthorize(Roles = "Billing")]
[TraxMutation(GraphQLOperation.Queue, Namespace = "billing")]
[TraxBroadcast]
public class IssueInvoiceTrain : ServiceTrain<IssueInvoiceInput, IssueInvoiceOutput>, IIssueInvoiceTrain { /* ... */ }

[TraxAuthorize]
[TraxQueryModel(Namespace = "billing")]
public class Invoice { /* ... */ }
```

The query model also needs its context on the GraphQL builder (`.AddDbContext<InvoicingDbContext>()`).
A Billing key queues `dispatch { billing { issueInvoice(...) } }` and follows the run with
`onTrainStateChanged`; any key reads `discover { billing { invoices { ... } } }`; only an Operator key
reaches `operations`. Without `[TraxBroadcast]` a Billing subscriber is refused with
`TRAX_AUTHORIZATION`, operations or not.

`MyAuditSink` is your [`ITraxAuditSink`](/docs/sdk-reference/api-audit/i-trax-audit-sink): the sample's writes each entry to a table. Every `[TraxQuery]`/`[TraxMutation]` train and `[TraxQueryModel]` entity then declares `[TraxAuthorize]` (optionally with a policy or roles) or `[TraxAllowAnonymous]`. A caller who fails a gate gets `TRAX_AUTHORIZATION` with the message `"Not authorized."`, and the train does not run.

### What Refuses Startup

Trax refuses to start a host whose security posture is missing or contradictory, rather than serving it open. Each refusal names what to change.

| Cause | Fix |
|---|---|
| A `[TraxQuery]`/`[TraxMutation]` train or `[TraxQueryModel]` entity with neither `[TraxAuthorize]` nor `[TraxAllowAnonymous]` (on an endpoint without `RequireAuthorization()`) | Add one of the two. See [Required Exposure Posture](/docs/authorization#required-exposure-posture) |
| An entity a query model reaches through a navigation, or a field an `[ExtendObjectType]` adds to a public or root type, with no posture | Add `[TraxAuthorize]` or `[TraxAllowAnonymous]` to it. See [Fields Added by a Type Extension](/docs/authorization#fields-added-by-a-type-extension) |
| Both markers on one surface, or `[TraxAllowAnonymous]` under `RequireAuthorization()` | Pick one |
| HotChocolate's `[Authorize]` or `[AllowAnonymous]` on a surface | Use `[TraxAuthorize]` / `[TraxAllowAnonymous]` |
| `ExposeOperationQueries()`/`ExposeOperationMutations()` with no gate | `GateOperations(policy, roles)`, `RequireAuthorization()` or `AllowAnonymousOperations()`. See [Gating the operations namespace](/docs/sdk-reference/graphql-api/add-trax-graphql#gating-the-operations-namespace) |
| `GateOperations()` with no policy and no roles | Name a role or policy, or call `GateOperationsToAuthenticatedUsers()` deliberately |
| The operations namespace exposed without `IOperationsService` / `ITraxScheduler` / `IJobSubmitter` | `AddScheduler(...)` on the same host |
| A demo API key (containing `do-not-use-in-production`) registered outside Development | Register demo keys only inside `IsDevelopment()`; use `AddHashed` or a resolver elsewhere |
| A symmetric JWT signing key containing `do-not-use-in-production` registered outside Development | Register the demo signing key only inside `IsDevelopment()`; load the real key from a secret store, or use `UseAuthority` |
| A policy that is not registered, named by `RequireAuthorization(policy)`, `GateOperations(policy: ...)`, a query model, a train, a type extension's resolver, or any `@authorize` directive in the schema | `AddAuthorization(o => o.AddPolicy(...))`, or correct the name |
| A `[TraxAuthorize]` train with no `ITrainAuthorizationService` | `AddTraxGraphQL()` registers one; a scheduler-only host calls `AllowMissingAuthorizationService()` |
| A junction that injects `TraxPrincipal` on a host where no `AddTrax*Auth` ran | `services.AddTraxPrincipalAccessor()`. See [Injecting TraxPrincipal](/docs/sdk-reference/api-auth/injecting-trax-principal#when-no-scheme-is-registered) |

Not refused at startup: an `AddTrax*Auth` call missing altogether (every gated surface then refuses every caller). A request that still meets a directive naming an unknown policy, on a host that skipped startup, is refused with `TRAX_AUTHORIZATION`, and the policy name goes to the log, never to the caller.

## API-Key Authentication

`Trax.Api.Auth.ApiKey` wraps an ASP.NET Core `AuthenticationHandler`. Every request that presents a configured header (default `X-Api-Key`) is resolved by an `ITraxPrincipalResolver<string>` into a `TraxPrincipal`. Missing header returns `NoResult`, letting `[AllowAnonymous]` routes keep working. Present but unresolved returns `Fail`.

Static key set (the common case):

```csharp
using Trax.Api.Auth.ApiKey;

if (builder.Environment.IsDevelopment())
{
    services.AddTraxApiKeyAuth(keys => keys
        .Add("admin-key-do-not-use-in-production",  id: "admin",  "Admin", "Player")
        .Add("player-key-do-not-use-in-production", id: "player", "Player"));
}
```

Cleartext keys in source are demo keys. A key containing `do-not-use-in-production`, an API key or a symmetric JWT signing key, makes the host refuse to start outside Development, so a copied demo key cannot go live by accident; production keys come from a secret manager (below).

Keys registered through the builder are salted and SHA-256 hashed at startup, then compared with `CryptographicOperations.FixedTimeEquals` on every request. The resolver iterates every entry without short-circuiting, so lookup cost is independent of which (if any) entry matches. Cleartext comparison is not reachable from consumer code.

Resolver class (for keys backed by a database, cache, or HTTP service):

```csharp
services.AddTraxApiKeyAuth<MyApiKeyResolver>();
```

The handler runs the resolver on every request that presents credentials, so cache if your resolver hits I/O.

### Pre-Hashed Keys (Production)

Production hosts should load salt and hash bytes from a secret manager so cleartext never enters the process:

```csharp
var admin = builder.Configuration.GetSection("ApiKeys:Admin");

services.AddTraxApiKeyAuth(keys => keys.AddHashed(
    salt:   Convert.FromBase64String(admin["Salt"]!),
    sha256: Convert.FromBase64String(admin["Hash"]!),
    id:     "admin",
    "Admin"));
```

For a principal that needs a display name distinct from its id, or custom claims, both `Add` and `AddHashed` have `Func<TraxPrincipal>` overloads.

### Header Handling

The handler rejects:

- Requests with multiple `X-Api-Key` headers (`AuthenticateResult.Fail`).
- Requests with a single `X-Api-Key` value containing a comma (reverse proxies sometimes coalesce duplicate headers per RFC 7230 §3.2.2; those are ambiguous and never forwarded to the resolver).

Missing or whitespace-only headers return `AuthenticateResult.NoResult()` so `[AllowAnonymous]` routes keep working.

## JWT Bearer Authentication

`Trax.Api.Auth.Jwt` is a thin wrapper around `Microsoft.AspNetCore.Authentication.JwtBearer`. Token validation (signature, issuer, audience, lifetime) runs through Microsoft's handler. After validation, Trax runs an `ITraxPrincipalResolver<JwtTokenInput>` to project the validated token into a `TraxPrincipal`.

For tokens from an OIDC provider (Auth0, Okta, Entra, Cognito):

```csharp
services.AddTraxJwtAuth(jwt => jwt.UseAuthority(
    authority: "https://login.example.com",
    audience:  "my-api"));
```

For self-issued tokens:

```csharp
services.AddTraxJwtAuth(jwt => jwt.UseSymmetricKey(
    issuer:   "https://trax.internal",
    audience: "my-api",
    key:      Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"]!)));
```

The default resolver maps `sub` (or `nameidentifier`) to `TraxPrincipal.Id`, `name` or `preferred_username` to `DisplayName`, and `role` / `roles` / `ClaimTypes.Role` claims to `Roles`. Other claims pass through on the custom claim bag. For custom mapping (database enrichment, token revocation checks), supply `ITraxPrincipalResolver<JwtTokenInput>` via `AddTraxJwtAuth<TResolver>`.

## OpenID Connect Authentication

`Trax.Api.Auth.Oidc` wires the browser-facing OIDC code flow with PKCE. A challenge against `OidcDefaults.SchemeName` redirects to the identity provider; on callback, the handler validates the id-token and signs the user into a session cookie (`OidcDefaults.CookieSchemeName`). Subsequent requests authenticate against the cookie.

```csharp
services.AddTraxOidcAuth(oidc => oidc
    .UseAuthority("https://login.example.com", "my-client-id")
    .WithClientSecret(builder.Configuration["Oidc:ClientSecret"]!)
    .AddScope("email"));

app.MapGet("/login", () =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = "/" },
        new[] { OidcDefaults.SchemeName }));
```

Unauthenticated API requests against the cookie scheme return `401` (not a redirect to `/Account/Login`). For non-browser clients that present bearer tokens, use `AddTraxJwtAuth` instead; OIDC is for the interactive sign-in path.

## Subscription Authentication

Browsers cannot attach custom headers to a WebSocket upgrade, so header-bound schemes (API key, JWT bearer) carry credentials in the `connection_init` payload instead. Cookie-bound schemes (OIDC) need no special handling: the browser attaches cookies to the upgrade request, so the cookie middleware authenticates the upgrade like any HTTP request.

`AddTraxGraphQL` registers one socket interceptor, `TraxCompositeSocketInterceptor`, for every host. It reads which schemes are registered from the finished container on the first connection, so authentication may be registered before or after `AddTraxGraphQL`, and it hands each connection to the API-key or JWT strategy by the credential the payload carries. See [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions#authentication) for the routing rules. Supplying your own interceptor through `ConfigureSchema` replaces it.

```csharp
services.AddTraxApiKeyAuth(...);   // or AddTraxJwtAuth / AddTraxJwtDispatcher
services.AddTraxGraphQL(...);      // reads what is registered above it
```

### API key

```js
const ws = new WebSocket("wss://host/trax/graphql", "graphql-transport-ws");
ws.onopen = () => ws.send(JSON.stringify({
    type: "connection_init",
    payload: { authToken: "admin-key-do-not-use-in-production" }  // or "apiKey"
}));
```

When `AddTraxApiKeyAuth` is registered, API-key connections go to `TraxApiKeySocketInterceptor`. It resolves the token via the same `ITraxPrincipalResolver<string>` used by the REST handler, attaches the resulting principal to `HttpContext.User` for the socket lifetime, and rejects the connection when the token is missing or invalid. While the connection is open, the key is resolved again every five minutes (`RecheckConnectionCredentialsEvery(...)`), and the connection is closed with code 1008 once the key no longer resolves to the same principal. See [Connection lifetime](/docs/sdk-reference/graphql-api/subscriptions#connection-lifetime).

### JWT bearer

```js
const ws = new WebSocket("wss://host/trax/graphql", "graphql-transport-ws");
ws.onopen = () => ws.send(JSON.stringify({
    type: "connection_init",
    payload: { authToken: "<jwt>" }  // or "bearer"
}));
```

When `AddTraxJwtAuth` is registered, JWT connections go to `TraxJwtSocketInterceptor`. It validates the token against the same `JwtBearerOptions` (signature, issuer, audience, lifetime, clock skew) the HTTP handler uses - the WS and HTTP paths cannot diverge. This includes Authority/JWKS schemes (Cognito, Google, any OIDC provider): the interceptor fetches signing keys from the scheme's discovery document when the options carry no static key. It then runs `ITraxPrincipalResolver<JwtTokenInput>` and attaches the resulting principal.

### OIDC cookie

No extra code required. The browser attaches the session cookie (`trax.oidc`) to the WebSocket upgrade request; ASP.NET Core's cookie middleware reads and validates it on the upgrade, and `HttpContext.User` is populated for the socket lifetime. This is the one genuinely symmetric path across HTTP and WS. The connection closes with code 1008 when the sign-in it was opened with expires.

### Allowed origins

A WebSocket upgrade that carries an `Origin` header is accepted only from origins the host serves: the endpoint's own host, or an allowed origin. Anything else is answered with `403` before the handshake. An upgrade with no `Origin` header, which is what non-browser clients send, is accepted. The allowed origins default to those your CORS default policy (`AddCors(o => o.AddDefaultPolicy(...))`) names with `WithOrigins(...)` or admits with `SetIsOriginAllowed(...)`. A default policy built with `AllowAnyOrigin()` allows no origin besides the endpoint's own: a browser sends its cookies with every WebSocket upgrade, so a socket is always a credentialed request, and CORS never lets `AllowAnyOrigin()` carry credentials. Set the origins explicitly with `AllowSocketOrigins(...)` on the GraphQL builder, which replaces the CORS default:

```csharp
services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<AppDbContext>()
    .AllowSocketOrigins("https://app.example.com", "https://admin.example.com"));
```

The endpoint's own host is compared without its scheme, so an https page served through a TLS-terminating proxy is recognised. The check applies wherever the Trax schema serves a socket, whether `UseTraxGraphQL()` maps it or you map it yourself with `MapGraphQL(path, "trax")`, and it does not apply to another HotChocolate schema on the same host. A host whose browser clients are on another origin, and whose CORS policy is a named one or allows any origin, needs `AllowSocketOrigins(...)`.

### Multiple JWT issuers

`AddTraxJwtDispatcher` routes subscription tokens by their `iss` claim, the same way it routes HTTP requests. When a dispatcher is registered, JWT connections go to `TraxJwtDispatcherSocketInterceptor` instead of the single-scheme JWT strategy, so each connection validates against the scheme its issuer maps to. Unmapped issuers are rejected.

### Custom interceptor

To authenticate against a scheme Trax does not model, supply your own `ISocketSessionInterceptor` through `ConfigureSchema`:

```csharp
services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<AppDbContext>()
    .ConfigureSchema(b => b.AddSocketSessionInterceptor<MySocketInterceptor>()));
```

This replaces Trax's interceptor and is independent of auth-registration order. Derive from `DefaultSocketSessionInterceptor` and read the credential from the `connection_init` payload in `OnConnectAsync`.

## Per-Train Authorization

`TraxPrincipalExtensions.ToClaimsPrincipal` populates both `trax:principal-id` (the resolver's id qualified by the scheme, `{scheme}:{id}`, so two issuers' subjects never collide; see [Qualified Principal Ids](/docs/migration-guides/qualified-principal-ids)) and `ClaimTypes.Name`, so the existing `[TraxAuthorize]` machinery from [Authorization](/docs/authorization) works unchanged. Policies and roles behave exactly as ASP.NET Core documents them. Role comparison is exact and case-sensitive, as `IsInRole` makes it.

### Error Messages are Generic

Trax deliberately never leaks which train a request tried to reach, which policy failed, or which role was required. A denied request returns a GraphQL error with code `TRAX_AUTHORIZATION` and the message `"Not authorized."` - nothing more. A request for an unknown train name returns `TRAX_TRAIN_NOT_FOUND` with a generic message; the requested name never echoes back. Diagnostic detail lives only in server-side logs. [Error Codes](/docs/sdk-reference/graphql-api/error-codes) lists every code the endpoint returns.

## Accessing the Current User

Junctions and application services that need the authenticated user inject `TraxPrincipal` directly:

```csharp
public class SendMessageJunction(TraxPrincipal user) : Junction<SendMessageInput, SentMessage>
{
    public override Task<SentMessage> Run(SendMessageInput input) =>
        service.SendAsync(user.Id, user.DisplayName, input.Body);
}
```

No `IHttpContextAccessor` plumbing. Every Trax auth scheme registers a scoped `TraxPrincipal` factory that resolves the current request's principal. Gate the upstream endpoint with `[TraxAuthorize]` and the junction can trust that the principal is present. See [Injecting TraxPrincipal](/docs/sdk-reference/api-auth/injecting-trax-principal) for dual-path (API + scheduler) patterns.

## GraphQL Hardening Defaults

`AddTraxGraphQL` installs resource-exhaustion guards by default. All are tunable via the builder.

```csharp
services.AddTraxGraphQL(graphql => graphql
    .MaxExecutionDepth(8)                   // default: 15
    .MaxOperationsPerRequest(25)            // default: 50
    .MaxOperationsPerConnection(20)         // default: 100
    .MaxConnectionLifetime(TimeSpan.FromMinutes(30))  // default: 1 hour
    .AllowIntrospection(ctx => IsInternalIp(ctx))   // default: Development only
    .ConfigureCost(opts => opts.MaxFieldCost = 2000)); // default: 1000
```

| Guard | Default | Purpose |
|---|---|---|
| `MaxExecutionDepth` | 15 | Rejects nested queries deeper than this (introspection fields excluded). |
| `MaxFieldCost` | 1000 | HotChocolate cost-analyzer ceiling. Prevents expensive field combinations. |
| `DefaultResolverCost` | 10 | Base cost applied to each resolver in the cost analyzer. |
| Introspection | On in Development, off elsewhere | Prevents anonymous schema enumeration in production. Decided per request on every transport (HTTP POST, GET, multipart, WebSocket). A predicate passed to `AllowIntrospection` replaces the default in every environment, Development included, and receives the request's `HttpContext`, so it can check the caller. The schema download (`?sdl`, `/schema`, `/schema.graphql`) and the GraphQL IDE follow the same answer and return 404 when it is no; an allowed download is sent `Cache-Control: private`. |
| `MaxOperationsPerRequest` | 50 | Caps the operations one request invokes: root fields, and the fields under each namespace (`dispatch`, `discover`, `operations`, nested namespaces such as `operations { deadLetters }`, and a train's declared `Namespace`). The namespace field itself is not counted, so `dispatch { a: refund(...) b: refund(...) }` counts two. Aliases and batched operations count, selections inside fragment spreads and inline fragments count as if written in place, and selections sharing a response path count once. Rejects with `TRAX_TOO_MANY_OPERATIONS` during validation, before any resolver runs. |
| `MaxOperationsPerConnection` | 100 | Caps the operations one WebSocket connection runs at once. An operation started past it gets `TRAX_SOCKET_OPERATION_LIMIT` and takes no place; the connection stays open, and a place frees when one of its operations completes. It is per connection, so it does not bound how many connections a client opens. |
| `MaxConnectionLifetime` | 1 hour | Closes every WebSocket connection with code 1001 (Going Away) at this age, at most one day. A JWT connection closes earlier at its token's `exp`, a cookie connection at its sign-in's expiry, and an API-key connection once a re-check (every five minutes, `RecheckConnectionCredentialsEvery`) finds its key revoked or changed. See [Connection lifetime](/docs/sdk-reference/graphql-api/subscriptions#connection-lifetime). |
| `operations` namespace | Off (queries and mutations) | The predefined `operations.*` queries (manifests, executions, dead letters, health, hosts, config) and mutations (trigger, cancel, requeue) are not exposed unless the consumer opts in via `ExposeOperationQueries()` / `ExposeOperationMutations()`. The mutation surface drives `ITraxScheduler` directly, so leaving it open lets any caller disrupt scheduled work, and the read surface discloses internal hostnames and per-instance execution counts. Exposing either without a gate fails at startup: answer with `GateOperations(policy, roles)` to gate the namespace alone, `RequireAuthorization()` to gate the whole endpoint, or `AllowAnonymousOperations()` to acknowledge a deliberately public control plane. The gate is the only check on manifest triggers and dead-letter requeues; `queueTrain` and `requeueExecution` also apply the train's `[TraxAuthorize]` requirements (see [The Operations Surface](/docs/authorization#the-operations-surface)). Train inputs (an execution's `input`, a manifest's `properties`, a work queue entry's `input`) and effect settings, any of which can hold credentials, answer to the same gate and are served one row at a time by the detail reads, never by a list (see [Train inputs and the operations gate](/docs/sdk-reference/graphql-api/queries#train-inputs-and-the-operations-gate)). |
| HTTP GET | Off | GraphQL runs over POST only. A cross-site top-level navigation carries a `SameSite=Lax` cookie, so a GET-executable query could run a `[TraxQuery]` train as the signed-in user. `AllowGetRequests()` opts in; an opted-in GET still needs the `GraphQL-preflight` header and runs queries only. See [Serving GraphQL over GET](#serving-graphql-over-get). |

### Serving GraphQL over GET

GraphQL over HTTP GET is off. A browser attaches a `SameSite=Lax` cookie to a cross-site top-level navigation, so with GET on, a link on another site could run a `[TraxQuery]` train as the signed-in user. The response is not readable cross-site, but the train still runs.

A host with a client that needs GET, such as a CDN caching persisted queries by id, opts in:

```csharp
services.AddTraxGraphQL(graphql => graphql.AllowGetRequests());
```

Two guards stay on. A GET must carry the `GraphQL-preflight: 1` header, which a navigation cannot add, and only queries run over GET; a mutation is refused. The setting belongs to the `trax` schema, so it holds for `UseTraxGraphQL()` and for a directly mapped `MapGraphQL(path, "trax")` alike. The IDE page and the SDL download are separate HotChocolate options and are unaffected.

### Gating GraphQL Execution Without Locking the IDE

Endpoint-level `RequireAuthorization` blanket-gates the route, including the GET that serves the GraphQL IDE. Developers can't even load the IDE to paste a key. The builder method splits these concerns:

```csharp
services.AddTraxGraphQL(graphql => graphql.RequireAuthorization());
```

The policy is checked in HotChocolate's request pipeline, so it applies only when the request is an actual GraphQL operation. The IDE's HTML shell, the schema download and CORS preflights are not affected by it; the IDE and the schema download follow `AllowIntrospection` instead, so outside Development they are served only when your predicate allows the request. POST queries and mutations are checked against the policy and rejected with a GraphQL error, with status 400:

```json
{ "errors": [{ "message": "Not authorized.", "extensions": { "code": "TRAX_AUTHORIZATION" } }] }
```

The error renders inline in the IDE result pane and matches the shape of per-train `[TraxAuthorize]` failures. The refusal happens inside execution, so the [audit pipeline](#audit-pipeline) records it as an unsuccessful entry.

The default policy is the combined `TraxAuthClaimTypes.TraxAuthPolicy`, which every `AddTrax*Auth` extension contributes its scheme to. When multiple schemes are registered (API key + JWT, etc.), any one of them is sufficient. To require a specific scheme:

```csharp
services.AddTraxGraphQL(graphql => graphql.RequireAuthorization(ApiKeyDefaults.PolicyName));
```

Subscription auth is a separate path: the `connection_init` payload is checked by `TraxCompositeSocketInterceptor`, which `AddTraxGraphQL` registers. `RequireAuthorization` only governs HTTP execution. If the policy isn't actually registered at startup, the host fails fast with a message naming the policy and pointing to `AddTraxApiKeyAuth`.

### Per-Principal Concurrency

`PerPrincipalMaxConcurrentRun(int)` on `TraxMediatorBuilder` caps the number of concurrent `RunAsync` executions for any single authenticated principal. Requests that exceed the cap queue on a per-principal semaphore until in-flight work completes.

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connStr))
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(Program).Assembly)
        .PerPrincipalMaxConcurrentRun(10)));
```

The cap bucket key is the `trax:principal-id` claim. Anonymous callers (no authenticated user) are not subject to the cap because they have no stable identity to bucket against. Scheduler and remote-worker calls bypass the cap entirely (no HttpContext).

### Input Size Cap

`WithMaxInputJsonBytes(int)` on `TraxMediatorBuilder` caps the UTF-8 byte length of caller-supplied train input JSON. Default is 256 KiB. Oversize inputs are rejected with `TrainInputValidationException` (code `TRAX_INVALID_INPUT`) after authorization runs but before deserialization, so attacker-controlled JSON never reaches the deserializer.

Input is read without JSON reference handling: `$id`, `$ref` and `$values` are not honoured, so the parsed input is the tree the caller sent. An enqueue also caps the input as it is stored on the work queue entry (indented, every member written) at 4 times `MaxInputJsonBytes`, and refuses a larger one with the same exception before the entry is written. Writing that form stops the moment it crosses the cap, so an oversized one is never built in full. See [TrainInputReader](/docs/sdk-reference/mediator-api/train-execution#traininputreader).

### OnQueue Time Limit

`WithMaxQueueHookDuration(TimeSpan)` on `TraxMediatorBuilder` bounds how long a train's `OnQueue` hook may hold its enqueue's pooled connection and open transaction. Default is 30 seconds. Past it the enqueue fails with `QueueHookTimeoutException` and releases the connection, so hooks that wait on something slow cannot drain the pool for every other enqueue. See [OnQueue](/docs/core/trains-and-junctions#onqueue-enqueue-time-hook).

## Audit Pipeline

`Trax.Api.GraphQL.Audit` is a HotChocolate `ExecutionDiagnosticEventListener` that captures each request, serializes it into a `TraxAuditEntry`, and enqueues to a bounded channel. A background writer drains the channel in batches and hands them to your `ITraxAuditSink`. The request thread never blocks on the sink. A request the endpoint policy refuses is captured too, as an unsuccessful entry with the caller's principal, or `<anonymous>` when it had no credential. So is a subscription refused or failed when it subscribes: the skip options below drop only requests that succeeded.

Wiring:

```csharp
services.AddTraxGraphQL(graphql =>
    graphql.AddAudit<MyPostgresAuditSink>(opts =>
    {
        opts.ChannelCapacity = 10_000;
        opts.BatchSize = 50;
        opts.FlushInterval = TimeSpan.FromMilliseconds(500);
    })
);
```

| Option | Default | Purpose |
|---|---|---|
| `ChannelCapacity` | 10,000 | Bounded channel size. Drops increment `trax.audit.dropped`. |
| `BatchSize` | 50 | Max entries per sink invocation. |
| `FlushInterval` | 500ms | Max wait before flushing a partial batch. |
| `MaxDocumentLength` | 65,536 | Documents longer than this are cut, marked `...[truncated]`, and followed by `[selected fields: ...]`, every `Type.field` the operation selects. Padding ahead of the fields that matter cannot push them out of the record. |
| `MaxOperationNameLength` | 256 | Operation names longer than this are cut and marked `...[truncated]`. |
| `MaxErrorTextLength` | 4,096 | `ErrorText` longer than this is cut and marked `...[truncated]`. |
| `RecordErrorMessages` | false | Record error messages in `ErrorText`. Off, each error is recorded as its code and path. |
| `SkipIntrospection` | true | Drop introspection operations that succeeded (every top-level selection is `__schema`, `__type` or `__typename`). |
| `SkipSubscriptions` | true | Drop subscriptions that were accepted. One refused or failed when it subscribes is always recorded. |
| `DefaultPrincipalId` | `<anonymous>` | Used when the request has no Trax principal. |
| `MaxRetries` | 3 | Sink retry attempts before dropping a batch, from 0 to 100. Dropped entries increment `trax.audit.dropped`. |
| `RetryBackoff` | 100ms | Initial backoff, doubled on each retry and jittered. |
| `MaxRetryBackoff` | 30s | The longest wait between two retries, at most one hour. |

The host refuses to start when an option is out of range (`BatchSize` 0, a negative length, `RetryBackoff` above `MaxRetryBackoff`, and so on), with an `OptionsValidationException` naming the option.

An entry's `PrincipalId` is the scheme-qualified id (`TraxApiKey:alice`, `TraxJwt:alice`), and its `OperationName` is the request's `operationName` field or, when the client sends none, the name the document gives its only operation (`null` for an anonymous operation).

### What an Entry Records

An audit store is kept for a long time and read by more people than the API, so an entry records what was called and by whom, not the values the caller sent:

- **The document has its literals replaced.** Every string becomes `""` and every number `0`, wherever it appears: inline arguments, nested input objects, list items, variable defaults, and the arguments of a directive on an operation, variable, field, fragment spread, inline fragment or fragment definition. Field names, aliases, input field names, booleans, enum values and `null` are kept. `login(input: { user: "bob", password: "hunter2" })` is recorded as `login(input: { user: "", password: "" })`. This is the same transform Apollo uses for its operation signatures, except that input objects and lists keep their shape.
- **Variables are not recorded** unless you register an `ITraxAuditRedactor` that returns them. The default, `DefaultAuditRedactor`, returns `null`.
- **Error messages are not recorded** unless you set `RecordErrorMessages`. A message can quote what the caller sent: a resolver that throws `$"Password {password} was rejected."`, or a coercion error naming the value. By default `ErrorText` holds each error's code and path, `PASSWORD_REJECTED at login`, with `<masked>` for an error that has no code; an exception the pipeline raised is recorded as its type name. Apollo's usage reporting masks error messages by default for the same reason.

To record variables, register a redactor and keep only what is safe. It receives the variables as a `JsonObject`, with input objects as nested objects and lists as arrays, so it can remove a field at any depth:

```csharp
public sealed class SensitiveFieldRedactor : ITraxAuditRedactor
{
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "token", "apiKey", "secret",
    };

    public JsonObject? Redact(JsonObject? variables)
    {
        Strip(variables);
        return variables;
    }

    private static void Strip(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).Where(Sensitive.Contains).ToList())
                    obj.Remove(key);
                foreach (var (_, child) in obj)
                    Strip(child);
                break;
            case JsonArray array:
                foreach (var child in array)
                    Strip(child);
                break;
        }
    }
}

services.AddSingleton<ITraxAuditRedactor, SensitiveFieldRedactor>();
```

A removal list fails open for a field you did not think of. Where that matters, keep a list of fields to record instead. Error codes and paths are recorded as the resolvers set them: a resolver that puts an input value in an error code puts it in the audit, and so does any error message once you set `RecordErrorMessages`.

### Drops and Shutdown

Every request the listener sees is either handed to the sink, skipped by `SkipIntrospection` or `SkipSubscriptions`, or counted in `trax.audit.dropped` (and `TraxAuditChannel.TotalDropped`). An entry is dropped when the listener fails to capture or build it, when the channel is full, when the sink refuses its batch after `MaxRetries` retries, or when shutdown runs out of time.

Between retries the writer waits `RetryBackoff`, doubled on each attempt and capped at `MaxRetryBackoff`, at a random point in the upper half of that value, so writers on several nodes do not retry a shared sink in step. The writer is single-threaded: while it retries one batch the channel fills behind it, so a high `MaxRetries` trades drops during a long outage for drops from a full channel.

On graceful shutdown the writer stops accepting entries and writes every entry it already accepted. It has until the host's shutdown timeout (`HostOptions.ShutdownTimeout`, 30 seconds by default); the cancellation token the sink receives fires only then. What is still unwritten at that point, including a batch a sink is stuck on, is counted as dropped, and the host finishes stopping.

## Operational Hygiene

Trax does none of these for you:

- **Transport:** serve all GraphQL traffic over HTTPS. Never accept credentials over plaintext HTTP.
- **Key storage:** read API keys, JWT secrets, and DB credentials from a secret manager. Never commit them.
- **Rotation:** rotate keys on a schedule and on any suspected exposure. Invalidate in the resolver.
- **Rate limiting:** use ASP.NET Core's rate-limit middleware keyed on `trax:principal-id`. It is scheme-qualified (`TraxApiKey:alice`, `TraxJwt:alice`), so one person signing in two ways gets two buckets.
- **Introspection:** off outside Development by default. If you open it with `AllowIntrospection`, make the predicate check the caller rather than returning `true`.
- **Audit dashboards:** alert on non-zero `trax.audit.dropped`. A dropped entry is an invisible operation.
- **Redaction:** variables are not recorded by default. If you register an `ITraxAuditRedactor` to record them, remove every field that could carry a token, PII, or a secret, at any depth.

## SDK Reference

> [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth) | [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [AddTraxJwtDispatcher](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher) | [AddTraxOidcAuth](/docs/sdk-reference/api-auth/add-trax-oidc-auth) | [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal) | [Injecting TraxPrincipal](/docs/sdk-reference/api-auth/injecting-trax-principal) | [ITraxPrincipalResolver](/docs/sdk-reference/api-auth/i-trax-principal-resolver) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddAudit](/docs/sdk-reference/api-audit/add-audit) | [TraxAuditEntry](/docs/sdk-reference/api-audit/trax-audit-entry) | [ITraxAuditSink](/docs/sdk-reference/api-audit/i-trax-audit-sink) | [TraxAuditOptions](/docs/sdk-reference/api-audit/trax-audit-options)
