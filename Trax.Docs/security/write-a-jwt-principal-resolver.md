---
title: Write a JWT principal resolver
description: How to decide whether a JWT scheme needs its own principal resolver, and how to write and register one that enriches, rejects or remaps a validated token.
type: how-to
area: security
nav_order: 11
---

# Write a JWT principal resolver

A JWT scheme maps each validated token to a `TraxPrincipal` through an
`ITraxPrincipalResolver<JwtTokenInput>`. Without one it uses `DefaultJwtPrincipalResolver`, whose
claim mapping is on [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth#defaultjwtprincipalresolver).

## Check whether you need one

Google, Auth0, Entra, Cognito and Okta emit `sub`, `name` and role claims the default resolver
reads, so a host that only needs the token's own identity writes no resolver:

```csharp compile
using Trax.Api.Auth.Jwt;

services.AddTraxJwtAuth("https://login.example.com", "my-api");
```

Write one when:

- roles live in a claim the default does not read: Entra app roles, Okta group URIs, namespaced
  Auth0 claims;
- the principal needs data the token does not carry: a tenant, permissions, feature flags;
- unknown or suspended subjects must be refused: an allow-list, a revocation check;
- claims need reshaping: a namespace prefix stripped, several role sources merged.

For Cognito tokens, `CognitoJwtPrincipalResolver` already reads groups and federated identities
([UseCognito](/docs/sdk-reference/api-auth/use-cognito)).

## Write the resolver

Read the validated claims from `input.Principal`, look the subject up, and return `null` to refuse
it. `null` fails authentication:

```csharp compile=resolver
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;

public sealed record User(string Id, string DisplayName, string[] Roles, string TenantId, bool IsSuspended);

public interface IUsers
{
    Task<User?> FindAsync(string id, CancellationToken ct);
}

public sealed class TenantJwtResolver(IUsers users) : ITraxPrincipalResolver<JwtTokenInput>
{
    public async ValueTask<TraxPrincipal?> ResolveAsync(JwtTokenInput input, CancellationToken ct)
    {
        var sub = input.Principal.FindFirst("sub")?.Value;
        if (sub is null)
            return null;

        var user = await users.FindAsync(sub, ct);
        if (user is null || user.IsSuspended)
            return null;

        return new TraxPrincipal(user.Id, user.DisplayName, user.Roles,
            Claims: new Dictionary<string, string> { ["tenant"] = user.TenantId },
            PrincipalType: JwtDefaults.PrincipalType);
    }
}
```

Return the subject as `Id`, unqualified: the scheme adds its own name, so the principal's id
becomes `TraxJwt:{sub}`.

## Register it

Pass it as the type argument. It is registered scoped, so it can take a `DbContext` or other
scoped services:

```csharp compile=resolver
using Trax.Api.Auth.Jwt;

services.AddTraxJwtAuth<TenantJwtResolver>(jwt => jwt.UseAuthority("https://login.example.com", "my-api"));
```

On a host with several issuers, each named scheme takes its own resolver:
`AddTraxJwtAuth<TResolver>(schemeName, jwt => ...)`.

## SDK Reference

> [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [ITraxPrincipalResolver](/docs/sdk-reference/api-auth/i-trax-principal-resolver) | [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal)
