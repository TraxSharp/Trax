---
layout: default
title: API Security
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

Multiple schemes can coexist in a single host. Every `AddTrax*Auth` call contributes its scheme to the combined `TraxAuthClaimTypes.TraxAuthPolicy`, so endpoints protected by that policy accept credentials from any registered scheme.

## API-Key Authentication

`Trax.Api.Auth.ApiKey` wraps an ASP.NET Core `AuthenticationHandler`. Every request that presents a configured header (default `X-Api-Key`) is resolved by an `ITraxPrincipalResolver<string>` into a `TraxPrincipal`. Missing header returns `NoResult`, letting `[AllowAnonymous]` routes keep working. Present but unresolved returns `Fail`.

Static key set (the common case):

```csharp
services.AddTraxApiKeyAuth(keys => keys
    .Add("admin-key",  id: "admin",  "Admin", "Player")
    .Add("player-key", id: "player", "Player"));
```

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

### API key

```js
const ws = new WebSocket("wss://host/trax/graphql", "graphql-transport-ws");
ws.onopen = () => ws.send(JSON.stringify({
    type: "connection_init",
    payload: { authToken: "admin-key" }  // or "apiKey"
}));
```

`AddTraxApiKeyAuth` auto-registers `TraxApiKeySocketInterceptor` when the Trax GraphQL schema is present. The interceptor resolves the token via the same `ITraxPrincipalResolver<string>` used by the REST handler, attaches the resulting principal to `HttpContext.User` for the socket lifetime, and rejects the connection when the token is missing or invalid.

### JWT bearer

```js
const ws = new WebSocket("wss://host/trax/graphql", "graphql-transport-ws");
ws.onopen = () => ws.send(JSON.stringify({
    type: "connection_init",
    payload: { authToken: "<jwt>" }  // or "bearer"
}));
```

`AddTraxJwtAuth` auto-registers `TraxJwtSocketInterceptor` when the Trax GraphQL schema is present. The interceptor validates the token against the same `JwtBearerOptions` (signature, issuer, audience, lifetime, clock skew) the HTTP handler uses - the WS and HTTP paths cannot diverge. This includes Authority/JWKS schemes (Cognito, Google, any OIDC provider): the interceptor fetches signing keys from the scheme's discovery document when the options carry no static key. It then runs `ITraxPrincipalResolver<JwtTokenInput>` and attaches the resulting principal.

### OIDC cookie

No extra code required. The browser attaches the session cookie (`trax.oidc`) to the WebSocket upgrade request; ASP.NET Core's cookie middleware reads and validates it on the upgrade, and `HttpContext.User` is populated for the socket lifetime. This is the one genuinely symmetric path across HTTP and WS.

### Multiple JWT issuers

`AddTraxJwtDispatcher` routes subscription tokens by their `iss` claim, the same way it routes HTTP requests. When a dispatcher is registered, Trax wires `TraxJwtDispatcherSocketInterceptor` in place of the single-scheme JWT interceptor, so each connection validates against the scheme its issuer maps to. Unmapped issuers are rejected.

### Custom interceptor

To authenticate against a scheme Trax does not model, supply your own `ISocketSessionInterceptor` through `ConfigureSchema`:

```csharp
services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<AppDbContext>()
    .ConfigureSchema(b => b.AddSocketSessionInterceptor<MySocketInterceptor>()));
```

This overrides the stock interceptors and is independent of auth-registration order. Derive from `DefaultSocketSessionInterceptor` and read the credential from the `connection_init` payload in `OnConnectAsync`.

## Per-Train Authorization

`TraxPrincipalExtensions.ToClaimsPrincipal` populates both `trax:principal-id` and `ClaimTypes.Name`, so the existing `[TraxAuthorize]` machinery from [Authorization](/docs/authorization) works unchanged. Policies and roles behave exactly as ASP.NET Core documents them. Role comparison is case-insensitive.

### Error Messages are Generic

Trax deliberately never leaks which train a request tried to reach, which policy failed, or which role was required. A denied request returns a GraphQL error with code `TRAX_AUTHORIZATION` and the message `"Not authorized."` - nothing more. A request for an unknown train name returns `TRAX_TRAIN_NOT_FOUND` with a generic message; the requested name never echoes back. Diagnostic detail lives only in server-side logs.

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
    .AllowIntrospection(ctx => IsInternalIp(ctx))   // default: Development only
    .ConfigureCost(opts => opts.MaxFieldCost = 2000)); // default: 1000
```

| Guard | Default | Purpose |
|---|---|---|
| `MaxExecutionDepth` | 15 | Rejects nested queries deeper than this (introspection fields excluded). |
| `MaxFieldCost` | 1000 | HotChocolate cost-analyzer ceiling. Prevents expensive field combinations. |
| `DefaultResolverCost` | 10 | Base cost applied to each resolver in the cost analyzer. |
| Introspection | On in Development, off elsewhere | Prevents anonymous schema enumeration in production. |
| `MaxOperationsPerRequest` | 50 | Caps aliased + batched top-level selections per request. Rejects with `TRAX_TOO_MANY_OPERATIONS`. |
| `operations` namespace | Off (queries and mutations) | The predefined `operations.*` queries (manifests, executions, dead letters, health) and mutations (trigger, cancel, requeue) are not exposed unless the consumer opts in via `ExposeOperationQueries()` / `ExposeOperationMutations()`. The mutation surface drives `ITraxScheduler` directly, so leaving it open lets any caller disrupt scheduled work. Pair the opt-in with `RequireAuthorization()` so only authenticated callers reach it. |

### Gating GraphQL Execution Without Locking the IDE

Endpoint-level `RequireAuthorization` blanket-gates the route, including the GET that serves the Banana Cake Pop tool page. Developers can't even load the IDE to paste a key. The builder method splits these concerns:

```csharp
services.AddTraxGraphQL(graphql => graphql.RequireAuthorization());
```

This installs a HotChocolate `IHttpRequestInterceptor` that runs only when the request is an actual GraphQL operation. The BCP HTML shell, schema introspection (when allowed by `AllowIntrospection`), and CORS preflights are not affected. POST queries and mutations are checked against the policy and rejected with a GraphQL error:

```json
{ "errors": [{ "message": "Not authorized.", "extensions": { "code": "TRAX_AUTHORIZATION" } }] }
```

The error renders inline in the IDE result pane and matches the shape of per-train `[TraxAuthorize]` failures.

The default policy is the combined `TraxAuthClaimTypes.TraxAuthPolicy`, which every `AddTrax*Auth` extension contributes its scheme to. When multiple schemes are registered (API key + JWT, etc.), any one of them is sufficient. To require a specific scheme:

```csharp
services.AddTraxGraphQL(graphql => graphql.RequireAuthorization(ApiKeyDefaults.PolicyName));
```

Subscription auth is a separate path: the `connection_init` payload is checked by `TraxApiKeySocketInterceptor` (wired automatically by `AddTraxApiKeyAuth`). `RequireAuthorization` only governs HTTP execution. If the policy isn't actually registered at startup, the host fails fast with a message naming the policy and pointing to `AddTraxApiKeyAuth`.

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

## Audit Pipeline

`Trax.Api.GraphQL.Audit` is a HotChocolate `ExecutionDiagnosticEventListener` that captures each request, serializes it into a `TraxAuditEntry`, and enqueues to a bounded channel. A background writer drains the channel in batches and hands them to your `ITraxAuditSink`. The request thread never blocks on the sink.

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
| `MaxDocumentLength` | 65,536 | Documents longer than this get a `...[truncated]` marker. |
| `SkipIntrospection` | true | Drop `IntrospectionQuery` from the log. |
| `SkipSubscriptions` | true | Subscriptions don't fit the request/response model. |
| `DefaultPrincipalId` | `<anonymous>` | Used when the request has no Trax principal. |
| `MaxRetries` | 3 | Sink retry attempts before dropping a batch. |
| `RetryBackoff` | 100ms | Initial backoff, doubles on each retry. |

Scrub sensitive variables via `ITraxAuditRedactor`:

```csharp
services.AddSingleton<ITraxAuditRedactor, MyRedactor>();
```

## Operational Hygiene

Trax does none of these for you:

- **Transport:** serve all GraphQL traffic over HTTPS. Never accept credentials over plaintext HTTP.
- **Key storage:** read API keys, JWT secrets, and DB credentials from a secret manager. Never commit them.
- **Rotation:** rotate keys on a schedule and on any suspected exposure. Invalidate in the resolver.
- **Rate limiting:** use ASP.NET Core's rate-limit middleware keyed on `trax:principal-id`.
- **Introspection:** disable in production to prevent unauthenticated schema enumeration.
- **Audit dashboards:** alert on non-zero `trax.audit.dropped`. A dropped entry is an invisible operation.
- **Redaction:** implement `ITraxAuditRedactor` for every payload that could contain tokens, PII, or secrets.

## SDK Reference

> [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth) | [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [AddTraxJwtDispatcher](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher) | [AddTraxOidcAuth](/docs/sdk-reference/api-auth/add-trax-oidc-auth) | [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal) | [Injecting TraxPrincipal](/docs/sdk-reference/api-auth/injecting-trax-principal) | [ITraxPrincipalResolver](/docs/sdk-reference/api-auth/i-trax-principal-resolver) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddAudit](/docs/sdk-reference/api-audit/add-audit) | [TraxAuditEntry](/docs/sdk-reference/api-audit/trax-audit-entry) | [ITraxAuditSink](/docs/sdk-reference/api-audit/i-trax-audit-sink) | [TraxAuditOptions](/docs/sdk-reference/api-audit/trax-audit-options)
