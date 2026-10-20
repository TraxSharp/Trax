---
title: Secure a GraphQL host
description: "Build a Trax GraphQL host with API-key and JWT authentication, gated trains and query models, an operator-only operations namespace and an audit trail."
type: how-to
area: security
nav_order: 1
redirect_from:
  - /docs/api-security
---

# Secure a GraphQL host

<a id="no-warranty"></a>

> NO WARRANTY FOR SECURITY. Trax.Api.Auth and Trax.Api.GraphQL.Audit are provided AS-IS. Trax, its authors, and contributors are NOT LIABLE for any security breach, credential leak, data loss, or damage arising from systems built on top of these packages. Securing your deployment is the SOLE RESPONSIBILITY OF THE CONSUMER. Read the [security disclaimer](https://github.com/TraxSharp/Trax/blob/main/Trax.Api/SECURITY-DISCLAIMER.md) before shipping any of this.

This guide builds a host that accepts API keys and JWTs, serves a gated mutation, a query any
signed-in caller can run, an `operations` namespace only operators reach, and records every
request in an audit trail. It is the [Auth sample](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/Auth)'s shape, trimmed; the
sample's E2E suite proves each gate.

The defaults are closed. Until a surface declares who may reach it, the host does not start, and a
caller with no credential reaches nothing but what you mark public. The rules are on
[Authorization rules](/docs/security/authorization-rules) and the reasoning on
[Where Trax checks authorization](/docs/security/where-authorization-is-checked).

Packages: `Trax.Effect.Data.Postgres`, `Trax.Effect.Provider.Json`, `Trax.Mediator`,
`Trax.Scheduler`, `Trax.Api.GraphQL`, `Trax.Api.Auth.ApiKey`, `Trax.Api.Auth.Jwt`,
`Trax.Api.GraphQL.Audit`.

## 1. Write the host

```csharp compile=host
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

// Authentication. Demo credentials only in Development; real ones from configuration.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddTraxApiKeyAuth(keys => keys
        .Add("ops-key-do-not-use-in-production", id: "ops", "Operator")
        .Add("billing-key-do-not-use-in-production", id: "billing", "Billing"));
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
builder.Services.AddAuthentication();       // the calls above are conditional,
builder.Services.AddTraxPrincipalAccessor(); // so register what they would bring

// Every policy a gate names. Roles need no registration.
builder.Services.AddAuthorization(o =>
    o.AddPolicy("VerifiedEmail", p => p.RequireClaim("email_verified", "true")));

// Trax. AddScheduler backs the operations namespace.
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString).AddJson())
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler));

// GraphQL: operators only on `operations`, each other surface declares its own posture,
// every request audited.
builder.Services.AddTraxGraphQL(graphql => graphql
    .ExposeOperationQueries()
    .ExposeOperationMutations()
    .GateOperations(roles: "Operator")
    .AddAudit<AuditLogSink>());

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.Run();
```

What each part does, and where it can differ:

- **Credentials.** A key or signing key containing `do-not-use-in-production` refuses the host
  outside Development, so a copied demo credential cannot go live. In production, register API keys
  as a salt and SHA-256 hash from a secret store with `AddHashed`, or resolve them yourself with
  `AddTraxApiKeyAuth<TResolver>()`; take JWTs from your identity provider with
  `AddTraxJwtAuth(authority, audience)`. For tokens from several issuers use
  [AddTraxJwtDispatcher](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher); for interactive
  browser sign-in, [AddTraxOidcAuth](/docs/sdk-reference/api-auth/add-trax-oidc-auth). Every
  `AddTrax*Auth` call adds its scheme to one combined policy and none becomes the default scheme;
  Trax tries each registered scheme on a GraphQL request
  ([how](/docs/security/authorization-rules#which-scheme-authenticates-a-graphql-request)).
- **`AddAuthentication()` and `AddTraxPrincipalAccessor()`.** Needed only because the auth calls are
  conditional. With neither branch configured, the host still starts and every gated surface
  refuses every caller.
- **Policies.** Register each policy a `[TraxAuthorize]`, `GateOperations` or `RequireAuthorization`
  names; an unregistered one refuses the host.
- **The operations namespace.** Exposing it needs a gate. `GateOperations(roles: "Operator")` gates
  it alone and leaves the rest of the endpoint open. `AddScheduler` registers the services it runs
  on.
- **Middleware.** `UseAuthentication()` and `UseAuthorization()` come before `UseTraxGraphQL()`.

## 2. Declare each surface's posture

Every `[TraxQuery]`, `[TraxMutation]` or `[TraxBroadcast]` train and every `[TraxQueryModel]`
entity declares `[TraxAuthorize]` (optionally with a policy or roles) or `[TraxAllowAnonymous]`.
A queue-only mutation for the Billing role, whose callers can follow its runs over a subscription,
and a query any signed-in caller can run:

```csharp compile=host
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

[TraxAuthorize(Roles = "Billing")]
[TraxMutation(GraphQLOperation.Queue, Namespace = "billing")]
[TraxBroadcast]
public class IssueInvoiceTrain
    : ServiceTrain<IssueInvoiceInput, IssueInvoiceOutput>,
        IIssueInvoiceTrain
{
    protected override Task<Either<Exception, IssueInvoiceOutput>> Junctions() =>
        Chain<IssueInvoiceJunction>().Resolve();
}

[TraxAuthorize]
[TraxQuery(Namespace = "billing")]
public class ListInvoicesTrain
    : ServiceTrain<ListInvoicesInput, ListInvoicesOutput>,
        IListInvoicesTrain
{
    protected override Task<Either<Exception, ListInvoicesOutput>> Junctions() =>
        Chain<ListInvoicesJunction>().Resolve();
}
```

A Billing key queues `dispatch { billing { issueInvoice(...) } }` and follows the run with
`onTrainStateChanged`. Any authenticated caller runs `discover { billing { listInvoices { ... } } }`.
Only an Operator key reaches `operations`. Without `[TraxBroadcast]`, a Billing caller who is not
an operator would have nothing to receive and is refused when subscribing.

A `[TraxQueryModel]` entity declares its posture the same way, on its class, and so does every
entity it reaches through a navigation; its context goes on the builder with
`.AddDbContext<T>()`. `[TraxAuthorize]` decides who may read the type, not which rows: to scope the
rows a caller sees, add a query filter that reads the caller (see
[Owner-scoped contexts](/docs/effect/effect-providers/domain-data-contexts#owner-scoped-contexts)).
The rules for each kind of surface are on [Authorization rules](/docs/security/authorization-rules).

## 3. Write the audit sink

The audit pipeline hands entries to your sink in batches, off the request thread:

```csharp compile=host
using Trax.Api.GraphQL.Audit;

public sealed class AuditLogSink(ILogger<AuditLogSink> logger) : ITraxAuditSink
{
    public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
    {
        foreach (var entry in batch)
            logger.LogInformation(
                "{Principal} {Operation} ok={Success} {Error}",
                entry.PrincipalId,
                entry.OperationName,
                entry.Success,
                entry.ErrorText
            );
        return Task.CompletedTask;
    }
}
```

A production sink writes to a store of its own; the Auth sample's writes each entry to a table.

### What an entry records

An entry records who called what and whether it was allowed, with the document's literals blanked,
no variables, and error codes rather than messages. Refused requests are recorded too. The fields
are on [TraxAuditEntry](/docs/sdk-reference/api-audit/trax-audit-entry), the batching, retry and
truncation settings on [TraxAuditOptions](/docs/sdk-reference/api-audit/trax-audit-options).

## 4. Start the host

Run it. If a surface has no posture, a policy is not registered, the operations namespace has no
gate, or a demo credential is registered outside Development, the host stops with a message naming
what to change. [Startup refusals](/docs/security/startup-refusals) lists every one.

Then call it without a credential:

```bash
curl -s http://localhost:5000/trax/graphql -H 'Content-Type: application/json' \
  -d '{"query":"{ discover { billing { listInvoices { invoices { id } } } } }"}'
```

The answer is a GraphQL error with code `TRAX_AUTHORIZATION` and the message `"Not authorized."`.
Every refusal looks like that: it does not say which train, policy or role was involved, and an
unknown train name returns `TRAX_TRAIN_NOT_FOUND` without echoing it. Detail goes to the server log.
[Error codes](/docs/sdk-reference/graphql-api/error-codes) lists every code. With
`-H 'X-Api-Key: billing-key-do-not-use-in-production'` the query answers.

## Gate the whole endpoint instead

When nothing on the endpoint is public, gate every operation and drop the per-surface requirement:

```csharp compile
using Trax.Api.GraphQL.Extensions;

services.AddTraxGraphQL(graphql => graphql.RequireAuthorization());
```

The gate applies to every operation, over HTTP and over a subscription socket, and a refused
request is audited. The IDE page and the schema download are not behind it; they follow
`AllowIntrospection`, which is off outside Development. With no argument the gate is the combined
Trax policy, so any registered scheme is enough; pass `ApiKeyDefaults.PolicyName` or another
policy to require one. `[TraxAllowAnonymous]` on any exposed surface then refuses the host, since
nothing can be reached anonymously.

Gate through the builder, not only with an ASP.NET Core convention on the mapped route: the
startup checks count the builder's gate and do not see the convention.

## Authenticate subscriptions from a browser

A browser cannot put a header on a WebSocket upgrade, so an API-key or JWT client sends its
credential in the `connection_init` payload:

```js
const ws = new WebSocket("wss://host/trax/graphql", "graphql-transport-ws");
ws.onopen = () => ws.send(JSON.stringify({
    type: "connection_init",
    payload: { authToken: "<api key or jwt>" },
}));
```

`AddTraxGraphQL` registers the interceptor that reads it, for every scheme registered on the host,
in whatever order. An OIDC cookie needs nothing: the browser sends it on the upgrade. A connection
is closed when its token expires, its sign-in expires, or a periodic re-check finds its API key
revoked.

A browser on another origin is accepted only from an origin the host allows. The default is the
origins your CORS default policy names; to set them explicitly:

```csharp compile
using Trax.Api.GraphQL.Extensions;

services.AddTraxGraphQL(graphql => graphql
    .ExposeOperationQueries()
    .GateOperations(roles: "Operator")
    .AllowSocketOrigins("https://app.example.com", "https://admin.example.com"));
```

[Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) has the payload keys, the routing
between schemes, connection lifetime and how to supply your own interceptor.

## Record variables in the audit

Variables are not recorded unless you register a redactor that returns them. Remove anything that
can carry a secret, at any depth:

```csharp compile=redactor
using System.Text.Json.Nodes;
using Trax.Api.GraphQL.Audit;

public sealed class SensitiveFieldRedactor : ITraxAuditRedactor
{
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "token",
        "apiKey",
        "secret",
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
```

```csharp compile=redactor
using Trax.Api.GraphQL.Audit;

services.AddSingleton<ITraxAuditRedactor, SensitiveFieldRedactor>();
```

A removal list misses the field you did not think of. Where that matters, keep a list of fields to
record instead. See [ITraxAuditRedactor](/docs/sdk-reference/api-audit/i-trax-audit-redactor).

## Tighten the limits

`AddTraxGraphQL` caps query depth, cost, operations per request and per socket, and socket
lifetime, turns introspection off outside Development and serves GraphQL over POST only. Change
any of them on the builder:

```csharp compile
using Trax.Api.GraphQL.Extensions;

services.AddTraxGraphQL(graphql => graphql
    .ExposeOperationQueries()
    .GateOperations(roles: "Operator")
    .MaxExecutionDepth(8)
    .MaxOperationsPerRequest(25)
    .MaxOperationsPerConnection(20)
    .MaxConnectionLifetime(TimeSpan.FromMinutes(30))
    .AllowIntrospection(ctx => ctx.User.IsInRole("Developer")));
```

A predicate passed to `AllowIntrospection` decides in every environment, Development included, so
make it check the caller. The defaults and every builder method are on
[AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql). How many runs one principal
may have in flight, how large an input may be, and how long an `OnQueue` hook may hold its enqueue
are mediator settings: [Dispatch limits](/docs/mediator/dispatch-limits).

## Before production

Trax does none of these for you:

- Serve all GraphQL traffic over HTTPS, and never accept credentials over plain HTTP.
- Read API keys, JWT secrets and database credentials from a secret store. Never commit them.
- Rotate keys on a schedule and on any suspected exposure; a resolver can refuse a revoked key at
  once.
- Rate-limit with ASP.NET Core's middleware keyed on `trax:principal-id`. The id is scheme-qualified
  (`TraxApiKey:alice`, `TraxJwt:alice`), so one person signed in two ways has two buckets.
- Alert on a non-zero `trax.audit.dropped` (in process, `TraxAuditChannel.TotalDropped`). A dropped
  entry is an operation nobody can see.
- Protect the dashboard, if the host serves one, with its own gate; see
  [Add the dashboard to a host](/docs/dashboard).

## See also

- [Authorization rules](/docs/security/authorization-rules)
- [Where Trax checks authorization](/docs/security/where-authorization-is-checked)
- [Startup refusals](/docs/security/startup-refusals)
- [Injecting TraxPrincipal](/docs/security/inject-the-current-principal): reading the
  caller in a junction, including one the scheduler also runs.

## SDK Reference

> [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth) | [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [AddTraxJwtDispatcher](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher) | [AddTraxOidcAuth](/docs/sdk-reference/api-auth/add-trax-oidc-auth) | [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddAudit](/docs/sdk-reference/api-audit/add-audit) | [TraxAuditEntry](/docs/sdk-reference/api-audit/trax-audit-entry) | [ITraxAuditSink](/docs/sdk-reference/api-audit/i-trax-audit-sink) | [TraxAuditOptions](/docs/sdk-reference/api-audit/trax-audit-options)
