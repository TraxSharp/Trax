# Trax.Api authentication and audit

## Security disclaimer: read this first

> **NO WARRANTY FOR SECURITY.** Trax.Api.Auth and Trax.Api.GraphQL.Audit are provided AS-IS. Trax, its authors, and contributors are NOT LIABLE for any security breach, credential leak, data loss, or damage arising from systems built on top of these packages. Securing your deployment is the SOLE RESPONSIBILITY OF THE CONSUMER.

Trax auth is plumbing, not a security product. It does not vet the strength of your keys, rotate secrets, detect compromised credentials, enforce TLS, rate-limit abusers, detect replay attacks, or threat-model on your behalf, and it is not a substitute for a professional security review. MIT's `NO WARRANTY` clause is not a formality: if your deployment is breached, the fault and the fix are yours.

The full disclaimer, including the consumer responsibility checklist, ships in this package as `SECURITY-DISCLAIMER.md` and is on GitHub: [SECURITY-DISCLAIMER.md](https://github.com/TraxSharp/Trax.Api/blob/main/SECURITY-DISCLAIMER.md). Read it before you deploy.

## What these packages are

[Trax](https://traxsharp.net/docs) is a .NET framework for building trains (typed pipelines of junctions) with execution logging, scheduling and a GraphQL API. These packages connect ASP.NET Core authentication to the Trax GraphQL API (`Trax.Api.GraphQL`): every scheme projects the caller into a `TraxPrincipal`, which `[TraxAuthorize]` on a train checks and which junctions can inject.

| Package | What it does | Reference |
|---|---|---|
| `Trax.Api.Auth` | `TraxPrincipal`, `ITraxPrincipalResolver<T>` and the claim-type constants every scheme shares. Referenced by the scheme packages. | [TraxPrincipal](https://traxsharp.net/docs/sdk-reference/api-auth/trax-principal) |
| `Trax.Api.Auth.ApiKey` | Header-based API keys (`X-Api-Key` by default), salted and hashed at startup. | [AddTraxApiKeyAuth](https://traxsharp.net/docs/sdk-reference/api-auth/add-trax-api-key-auth) |
| `Trax.Api.Auth.Jwt` | JWT bearer tokens, validated by `Microsoft.AspNetCore.Authentication.JwtBearer`. | [AddTraxJwtAuth](https://traxsharp.net/docs/sdk-reference/api-auth/add-trax-jwt-auth) |
| `Trax.Api.Auth.Jwt.Cognito` | `UseCognito(...)` on the JWT builder: Amazon Cognito ID and access tokens and their claims. | [UseCognito](https://traxsharp.net/docs/sdk-reference/api-auth/use-cognito) |
| `Trax.Api.Auth.Jwt.Cognito.Issuer` | Mints Cognito-shaped RS256 tokens, with a refresh-token store contract. | [Cognito issuer](https://traxsharp.net/docs/sdk-reference/api-auth/cognito-issuer) |
| `Trax.Api.Auth.Jwt.Testing` | A self-hosted JWKS server and token minters for integration tests. | [JWT testing](https://traxsharp.net/docs/sdk-reference/api-auth/jwt-testing) |
| `Trax.Api.Auth.Oidc` | OpenID Connect code flow with PKCE and a session cookie, for browser sign-in. | [AddTraxOidcAuth](https://traxsharp.net/docs/sdk-reference/api-auth/add-trax-oidc-auth) |
| `Trax.Api.GraphQL.Audit` | Records each GraphQL request to your `ITraxAuditSink` from a bounded channel and a background writer. | [API Security](https://traxsharp.net/docs/api-security) |

## Installation

```bash
dotnet add package Trax.Api.GraphQL
dotnet add package Trax.Api.Auth.ApiKey    # or Trax.Api.Auth.Jwt, Trax.Api.Auth.Oidc
dotnet add package Trax.Api.GraphQL.Audit  # optional
```

## Example

An API-key scheme and a JWT scheme on one host, the GraphQL endpoint gated on either, and every request audited:

```csharp
using Trax.Api.Auth.ApiKey;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Audit;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(Program).Assembly)
);

// Keys come from your secret manager, never from source control.
builder.Services.AddTraxApiKeyAuth(keys => keys
    .Add(builder.Configuration["ApiKeys:Admin"]!, id: "admin", "Admin"));

builder.Services.AddTraxJwtAuth(jwt => jwt.UseAuthority(
    authority: "https://login.example.com",
    audience: "my-api"));

builder.Services.AddAuthorization();

// With no policy name, RequireAuthorization uses TraxAuthClaimTypes.TraxAuthPolicy, which
// every AddTrax*Auth call adds its scheme to: an API key or a JWT is accepted.
builder.Services.AddTraxGraphQL(graphql => graphql
    .RequireAuthorization()
    .AddAudit<MyAuditSink>());

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();   // maps at /trax/graphql

app.Run();

public sealed class MyAuditSink : ITraxAuditSink
{
    public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
    {
        // Persist the batch. Redact sensitive variables first with an ITraxAuditRedactor.
        return Task.CompletedTask;
    }
}
```

Per-train authorization uses `[TraxAuthorize]` on the train class; see [Authorization](https://traxsharp.net/docs/authorization). Subscriptions carry credentials in the `connection_init` payload; see [API Security](https://traxsharp.net/docs/api-security).

## Documentation

- [API Security](https://traxsharp.net/docs/api-security): every scheme, subscription auth, auditing and hardening defaults
- [API Auth reference](https://traxsharp.net/docs/sdk-reference/api-auth)
- [Trax documentation](https://traxsharp.net/docs)
- Source: [github.com/TraxSharp/Trax.Api](https://github.com/TraxSharp/Trax.Api)

## License

MIT, with the security disclaimer above. See [LICENSE](https://github.com/TraxSharp/Trax.Api/blob/main/LICENSE).
