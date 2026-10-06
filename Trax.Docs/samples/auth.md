---
layout: default
title: Auth
description: "The Auth sample: API keys and JWT side by side, TraxAuthorize with roles and policies, GateOperations, scheme-qualified principal ids and the audit trail."
parent: Samples & Deployment
nav_order: 2
---

# Auth

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

`Trax.Samples/samples/Auth/Trax.Samples.Auth` is one host that secures a Trax GraphQL server end
to end. Copy its `Program.cs` as the starting point for your own: everything a secured host needs
is in it, and nothing else.

| What it shows | Where |
|---|---|
| API keys and JWT bearer side by side, either one authenticating any request | `AddTraxApiKeyAuth`, `AddTraxJwtAuth` |
| Demo credentials that exist only in Development; real ones from configuration elsewhere | `if (builder.Environment.IsDevelopment())` |
| A public train and a public query model | `[TraxAllowAnonymous]` |
| A train any signed-in caller may run | bare `[TraxAuthorize]` |
| A policy and a role that must both pass | `[TraxAuthorize("VerifiedEmail")]` + `[TraxAuthorize(Roles = "Editor")]` |
| Either of two roles is enough | `[TraxAuthorize(Roles = "Editor,Auditor")]` |
| A gated entity reached through a public one | `Article.editorNote` |
| The `operations` namespace for operators only, the rest of the endpoint open | `GateOperations(roles: "Operator")` |
| Principal ids qualified by scheme | `TraxApiKey:alice`, `TraxJwt:alice` |
| Who did what, refused calls included, readable by auditors | `AddAudit<DatabaseAuditSink>()`, the `AuditRecord` query model |

Every row is proven against the real host by `tests/Trax.Samples.Auth.E2E`, over both schemes and
over WebSockets.

## Run it

The sample uses PostgreSQL. From the `Trax.Samples` root:

```bash
docker compose up -d database
dotnet run --project samples/Auth/Trax.Samples.Auth
```

`dotnet run` uses `Properties/launchSettings.json`, which sets `ASPNETCORE_ENVIRONMENT=Development`
and serves on `http://localhost:5220`. Development is the only environment in which the demo
credentials exist. The connection string is `ConnectionStrings:TraxDatabase` in `appsettings.json`
(`Host=localhost;Port=5432;Database=trax_auth;Username=trax;Password=trax123`); override it with the
`ConnectionStrings__TraxDatabase` environment variable.

## The demo users

Each user can sign in either way: with an API key in `X-Api-Key`, or with a JWT in
`Authorization: Bearer`. `GET /dev/token/{user}` mints a one-hour token, in Development only.

| User | API key | Roles | Email verified |
|---|---|---|---|
| `alice` | `alice-key-do-not-use-in-production` | `Editor` | yes |
| `erin` | `erin-key-do-not-use-in-production` | `Editor` | no |
| `bob` | `bob-key-do-not-use-in-production` | `Reader` | yes |
| `oscar` | `oscar-key-do-not-use-in-production` | `Operator`, `Auditor` | yes |

## Try it

Every command below was run against the sample. Responses are trimmed to the interesting part.

```bash
G=localhost:5220/trax/graphql
J='Content-Type: application/json'

# Public: no credential needed
curl -s $G -H "$J" -d '{"query":"{ discover { echo(input: { message: \"hi\" }) { echoed } } }"}'
# {"data":{"discover":{"echo":{"echoed":"hi"}}}}

# A gated train with no credential: the generic refusal, nothing else
curl -s $G -H "$J" -d '{"query":"{ discover { whoAmI { id } } }"}'
# {"errors":[{"message":"Not authorized.","path":["discover","whoAmI"],"extensions":{"code":"TRAX_AUTHORIZATION"}}],...}

# Alice by key, then by token: the id says which scheme authenticated her
curl -s $G -H "$J" -H 'X-Api-Key: alice-key-do-not-use-in-production' \
  -d '{"query":"{ discover { whoAmI { id roles principalType } } }"}'
# {"data":{"discover":{"whoAmI":{"id":"TraxApiKey:alice","roles":["Editor"],"principalType":"apikey"}}}}

TOKEN=$(curl -s localhost:5220/dev/token/alice | sed 's/.*"token":"\([^"]*\)".*/\1/')
curl -s $G -H "$J" -H "Authorization: Bearer $TOKEN" \
  -d '{"query":"{ discover { whoAmI { id roles principalType } } }"}'
# {"data":{"discover":{"whoAmI":{"id":"TraxJwt:alice","roles":["Editor"],"principalType":"jwt"}}}}

# Erin holds Editor but fails the VerifiedEmail policy: refused
curl -s $G -H "$J" -H 'X-Api-Key: erin-key-do-not-use-in-production' \
  -d '{"query":"mutation { dispatch { news { publishArticle(input: { title: \"t\", body: \"b\" }) { output { articleId } } } } }"}'
# {"errors":[{"message":"Not authorized.",...,"extensions":{"code":"TRAX_AUTHORIZATION"}}],...}

# Alice passes both
curl -s $G -H "$J" -H "Authorization: Bearer $TOKEN" \
  -d '{"query":"mutation { dispatch { news { publishArticle(input: { title: \"t\", body: \"b\" }) { output { articleId authorId } } } } }"}'
# {"data":{"dispatch":{"news":{"publishArticle":{"output":{"articleId":3,"authorId":"TraxJwt:alice"}}}}}}

# Articles are public; their editor notes are not
curl -s $G -H "$J" -d '{"query":"{ discover { news { articles { nodes { title editorNote { text } } } } } }"}'
# "editorNote": null on every node, with one TRAX_AUTHORIZATION error per node

# The operations namespace: refused to Alice, served to Oscar
curl -s $G -H "$J" -H 'X-Api-Key: alice-key-do-not-use-in-production' \
  -d '{"query":"{ operations { health { status } } }"}'
# {"errors":[{"message":"Not authorized.","path":["operations"],...}],"data":{"operations":null}}
curl -s $G -H "$J" -H 'X-Api-Key: oscar-key-do-not-use-in-production' \
  -d '{"query":"{ operations { health { status } } }"}'
# {"data":{"operations":{"health":{"status":"Healthy"}}}}

# Who did what. Entries reach the table within a quarter of a second of the request.
curl -s $G -H "$J" -H 'X-Api-Key: oscar-key-do-not-use-in-production' \
  -d '{"query":"{ discover { audit { auditRecords(first: 5, order: { id: DESC }) { nodes { principalId success errorText document } } } } }"}'
# {"principalId":"TraxApiKey:alice","success":false,"errorText":"TRAX_AUTHORIZATION at operations","document":"{\n  operations {..."}
```

## How it is built

### Authentication: two schemes, Development and everything else

```csharp
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Auth.Jwt;

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(DemoCredentials.AliceKey, DemoCredentials.Alice.ToApiKeyPrincipal)
            .Add(DemoCredentials.ErinKey, DemoCredentials.Erin.ToApiKeyPrincipal)
            .Add(DemoCredentials.BobKey, DemoCredentials.Bob.ToApiKeyPrincipal)
            .Add(DemoCredentials.OscarKey, DemoCredentials.Oscar.ToApiKeyPrincipal)
    );

    builder.Services.AddTraxJwtAuth(jwt =>
        jwt.UseSymmetricKey(
            DemoCredentials.JwtIssuer,
            DemoCredentials.JwtAudience,
            DemoCredentials.JwtSigningKey   // at least 32 bytes
        )
    );
}
else
{
    var apiKeys = builder.Configuration.GetSection("Auth:ApiKeys").Get<ConfiguredApiKey[]>() ?? [];
    if (apiKeys.Length > 0)
        builder.Services.AddTraxApiKeyAuth(keys =>
        {
            foreach (var key in apiKeys)
                keys.AddHashed(
                    Convert.FromBase64String(key.Salt),
                    Convert.FromBase64String(key.Hash),   // SHA-256(salt || UTF-8 key)
                    key.Id,
                    key.Roles
                );
        });

    var jwt = builder.Configuration.GetSection("Auth:Jwt");
    if (jwt["Authority"] is { Length: > 0 } authority)
        builder.Services.AddTraxJwtAuth(authority, jwt["Audience"]!);
}

builder.Services.AddAuthentication();
builder.Services.AddTraxPrincipalAccessor();

// A production API key as configuration holds it: never the key itself.
internal sealed record ConfiguredApiKey(string Id, string Salt, string Hash, string[] Roles);
```

- **Neither scheme is the default.** For a GraphQL request, Trax tries every registered scheme in
  registration order and keeps the first that authenticates. A request carrying both an API key
  and a token is authenticated by the API key here, because it is registered first.
- **The demo credentials exist only in Development.** Each demo API key contains
  `do-not-use-in-production`, and Trax.Api refuses to start a host outside Development with such
  a key registered: `AddTraxApiKeyAuth() registered a key containing 'do-not-use-in-production',
  which marks a published demo key, and the environment is 'Production'. Such keys start only in
  Development.` Every demo credential, the JWT signing key included, is registered inside that `if`.
- **Production reads real credentials from configuration.** `AddHashed` takes the salt and
  `SHA-256(salt || UTF-8 key)`, so the key itself never enters the process. See
  [Pre-hashed keys](/docs/sdk-reference/api-auth/add-trax-api-key-auth#pre-hashed-keys) for how to
  produce them. The JWT scheme validates tokens against the identity provider's published keys.
- **The last two lines matter because the registrations above are conditional.** Every
  `AddTrax*Auth` call registers the authentication services and the injectable `TraxPrincipal`.
  With none of them called (Production with nothing configured), `app.UseAuthentication()` throws
  `Unable to resolve service for type 'IAuthenticationSchemeProvider'`, and the mediator refuses a
  host whose junctions inject `TraxPrincipal`: `step 1 (DescribeCallerJunction) needs
  'Trax.Api.Auth.TraxPrincipal' as a constructor argument; ... the container does not register
  it.` With both lines, that host starts and refuses every gated call.

The factory overload of `Add` builds the whole principal. This one carries a custom claim, which
the `VerifiedEmail` policy reads, and sets `PrincipalType` to `apikey`, which the plain
`Add(key, id, roles)` overload sets for you:

```csharp
public TraxPrincipal ToApiKeyPrincipal() =>
    new(Id, DisplayName, Roles,
        Claims: new Dictionary<string, string> { ["email_verified"] = EmailVerified ? "true" : "false" },
        PrincipalType: "apikey");
```

A JWT carries the same facts as claims: `sub`, `name`, `role` (one per role) and
`email_verified`. Trax's default JWT resolver maps `sub` to the id, `name` to the display name and
`role` to roles, and passes `email_verified` through to the principal's claims.

### Authorization: policies and roles

```csharp
builder.Services.AddAuthorization(options =>
    options.AddPolicy("VerifiedEmail", policy => policy.RequireClaim("email_verified", "true"))
);
```

Roles need no registration. A policy does: register every policy before a `[TraxAuthorize]`
names it.

| Surface | Declaration | Who gets in |
|---|---|---|
| `EchoTrain` | `[TraxQuery]` `[TraxAllowAnonymous]` | anyone |
| `WhoAmITrain` | `[TraxQuery]` `[TraxAuthorize]` | any signed-in caller |
| `PublishArticleTrain` | `[TraxMutation(GraphQLOperation.Run, Namespace = "news")]` `[TraxAuthorize("VerifiedEmail")]` `[TraxAuthorize(Roles = "Editor")]` | an editor who passes the policy (Alice, not Erin) |
| `Article` | `[TraxQueryModel(Namespace = "news")]` `[TraxAllowAnonymous]` | anyone |
| `Article.wordCount` (type extension) | `[TraxAllowAnonymous]` on the resolver | anyone |
| `EditorNote` (reached through `Article.editorNote`) | `[TraxAuthorize(Roles = "Editor,Auditor")]` | an editor or an auditor |
| `AuditRecord` | `[TraxQueryModel(Namespace = "audit")]` `[TraxAuthorize(Roles = "Auditor")]` | an auditor |
| `operations` | `GateOperations(roles: "Operator")` | an operator |

Separate attributes combine as ASP.NET Core's `[Authorize]` does: each is a requirement of its own,
and the caller must meet every one, so policies AND and two role attributes require both roles.
Within one attribute, a comma-separated role list is any of. Every exposed train and query model declares exactly one of
`[TraxAuthorize]` and `[TraxAllowAnonymous]`; with neither, the host refuses to start, naming the
type. So do `EditorNote`, which is not a query model but is reachable from one, and the
`wordCount` field grafted onto the public `Article`.

A refusal is always the same response: code `TRAX_AUTHORIZATION`, message `Not authorized.`. The
train never runs, so a refused `publishArticle` writes no article.

`PublishArticleTrain` exposes `Run` only. Its junction reads the caller by injecting
`TraxPrincipal`, which needs the HTTP request; a queued run, executed later by the scheduler,
has none.

```csharp
public class SaveArticleJunction(TraxPrincipal caller, INewsroomDbContext db)
    : Junction<PublishArticleInput, PublishArticleOutput>
{
    public override async Task<PublishArticleOutput> Run(PublishArticleInput input)
    {
        var article = new Article { Title = input.Title, Body = input.Body, AuthorId = caller.Id };
        db.Articles.Add(article);
        await db.SaveChangesAsync();
        return new PublishArticleOutput(article.Id, article.AuthorId);
    }
}
```

`caller.Id` is the scheme-qualified id, `TraxJwt:alice` or `TraxApiKey:alice`. Store it as it is.

### GraphQL: the operations gate and the audit trail

```csharp
using Trax.Api.GraphQL.Audit;
using Trax.Api.GraphQL.Extensions;

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString).AddJson())
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler => scheduler)
);

builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .AddDbContext<NewsroomDbContext>()
        .AddTypeExtension<ArticleExtensions>()
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: "Operator")
        .AddAudit<DatabaseAuditSink>(audit =>
        {
            audit.BatchSize = 20;
            audit.FlushInterval = TimeSpan.FromMilliseconds(250);
        })
);

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
```

- **`GateOperations(roles: "Operator")`** puts the gate on the `operations` field of the query and
  the mutation root. The rest of the endpoint stays open, which `RequireAuthorization()` cannot
  do: it gates every operation, and also refuses to start beside a `[TraxAllowAnonymous]` surface.
  Exposing the namespace with no gate at all refuses startup, and so does calling
  `GateOperations()` with neither a policy nor roles.
- **`queueTrain` also applies the queued train's own `[TraxAuthorize]`.** Oscar is an operator but
  not an editor, so `operations { workQueue { queueTrain(input: { trainName:
  "Trax.Samples.Auth.Trains.IPublishArticleTrain", ... }) } }` is refused and writes nothing.
- **No `SaveTrainParameters()`.** A saved input is readable by anyone who passes the operations
  gate, through `operations { executionDetail { input } }`, and `publishArticle`'s input carries the
  editor note. Saving it would hand every operator a note that only editors and auditors may read.
- **`AddScheduler`** registers what the operations namespace runs on (`IOperationsService`,
  `ITraxScheduler`, a job submitter). Exposing the namespace without them refuses startup.
- **Subscriptions.** A WebSocket carries its credential as `authToken` in the `connection_init`
  payload: a JWT, or an API key. With no credential, an unknown key or a token that fails
  validation, the connection is refused. Because this host exposes the operations surface, an
  operator subscribed to `onTrainCompleted` receives every train, and Bob, who may see none of
  them (no train here is `[TraxBroadcast]`), is refused when he subscribes.

The sink stores each entry in a table of the sample's own `newsroom` schema:

```csharp
public sealed class DatabaseAuditSink(IDbContextFactory<NewsroomDbContext> contexts) : ITraxAuditSink
{
    public async Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        db.AuditRecords.AddRange(batch.Select(e => new AuditRecord
        {
            PrincipalId = e.PrincipalId, PrincipalType = e.PrincipalType,
            OperationName = e.OperationName, Document = e.Document,
            Success = e.Success, ErrorText = e.ErrorText,
            DurationMs = e.DurationMs, Timestamp = e.Timestamp.UtcDateTime,
        }));
        await db.SaveChangesAsync(ct);
    }
}
```

| Request | `PrincipalId` | `PrincipalType` | `Success` | `ErrorText` |
|---|---|---|---|---|
| `echo`, no credential | `<anonymous>` | null | true | null |
| `echo`, Alice's key | `TraxApiKey:alice` | `apikey` | true | null |
| `echo`, Alice's token | `TraxJwt:alice` | `jwt` | true | null |
| `publishArticle`, Erin's token | `TraxJwt:erin` | `jwt` | false | `Not authorized.` |
| `operations { health }`, Bob's key | `TraxApiKey:bob` | `apikey` | false | `Not authorized.` |

The document is stored with every string and number literal blanked (`title: ""`), and variables
are not recorded. `OperationName` is the request's `operationName` field, or, when a client sends none, the name
the document gives its operation (`query Feed { ... }` is recorded as `Feed`).

## What the tests prove

`tests/Trax.Samples.Auth.E2E` runs the real host through `WebApplicationFactory`, against the
`auth_e2e_tests` database. Locally, start Postgres and run
`dotnet test tests/Trax.Samples.Auth.E2E`; set `TRAX_TEST_PG_PORT` when your Postgres is not on 5432.

| Test class | Proves |
|---|---|
| `AuthenticationTests` | ids `TraxApiKey:alice` and `TraxJwt:alice`; an unknown key, a forged, expired or wrong-audience token is anonymous; the first registered scheme wins when both are sent |
| `TrainAuthorizationTests` | `publishArticle` refused to anonymous, readers, operators and the unverified editor, over both schemes, without running; served to Alice over both |
| `QueryModelAuthorizationTests` | public articles; the gated note through the public article, selected or filtered on; either stacked role reads it; the audit model is refused and cannot be counted without `Auditor` |
| `OperationsGateTests` | `operations` reads and mutations refused without `Operator`; served to Oscar over both schemes; `queueTrain` still applies the train's own gate; an operator reads no editor note from an execution's saved input |
| `AuditTrailTests` | every call recorded with its qualified principal, refused ones as unsuccessful, without literal values; an auditor reads it back over GraphQL |
| `SubscriptionAuthTests` | socket credentials accepted or refused; the operator sees every train, the reader is refused |
| `DemoCredentialsEnvironmentTests` | in Production no demo scheme exists and the demo key and token authenticate nobody, `/dev/token` is gone, a copied demo key refuses startup, and configured hashed keys and an identity provider's tokens work |

The Production test signs in with an identity provider's token using `TestJwksServer` from
`Trax.Api.Auth.Jwt.Testing`; see [JWT Testing](/docs/sdk-reference/api-auth/jwt-testing).

## SDK Reference

> [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth) | [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [Injecting TraxPrincipal](/docs/sdk-reference/api-auth/injecting-trax-principal) | [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxAllowAnonymous](/docs/sdk-reference/attributes/trax-allow-anonymous) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddAudit](/docs/sdk-reference/api-audit/add-audit) | [ITraxAuditSink](/docs/sdk-reference/api-audit/i-trax-audit-sink) | [TraxAuditEntry](/docs/sdk-reference/api-audit/trax-audit-entry) | [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context)

See also [Authorization](/docs/authorization), [API Security](/docs/api-security) and
[Qualified Principal Ids](/docs/migration-guides/qualified-principal-ids).
