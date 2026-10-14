---
title: Sign development JWTs
description: How to run a Trax host locally with a development JWT signing key and mint tokens for it, while keeping that key out of every other environment.
type: how-to
area: security
nav_order: 12
---

# Sign development JWTs

To try a host locally without an identity provider, validate tokens with a symmetric key that
lives in source and mint tokens with the same key. Anyone who reads that key can sign a token for
any user, so it must never validate tokens outside Development.

## Register the key in Development only

Put `do-not-use-in-production` in the key, register it only when the environment is Development,
and register the real issuer everywhere else:

```csharp fragment
using Trax.Api.Auth.Jwt;

if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxJwtAuth(jwt =>
        jwt.UseSymmetricKey(DevJwt.Issuer, DevJwt.Audience, DevJwt.Key));
else
    builder.Services.AddTraxJwtAuth(
        builder.Configuration["Auth:Jwt:Authority"]!,
        builder.Configuration["Auth:Jwt:Audience"]!);
```

The marker is a backstop: a host that validates with a symmetric key containing it refuses to
start outside Development, wherever the key was configured
([AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth#demo-signing-keys-start-only-in-development)).

## Mint tokens with it

Sign tokens with the claims `DefaultJwtPrincipalResolver` reads: `sub` becomes the id, `name` the
display name, each `role` a role:

```csharp compile
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

public static class DevJwt
{
    public const string Issuer = "my-app-dev";
    public const string Audience = "my-app";

    public static readonly byte[] Key =
        Encoding.UTF8.GetBytes("my-app-dev-signing-key-do-not-use-in-production");

    public static string Mint(string sub, params string[] roles) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity(
                [new Claim("sub", sub), new Claim("name", sub), .. roles.Select(r => new Claim("role", r))]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Key), SecurityAlgorithms.HmacSha256),
        });
}
```

The [Auth sample](/docs/samples/auth) serves such tokens from a `GET /dev/token/{user}` endpoint it
maps only in Development. In tests, [TestTokenIssuer](/docs/sdk-reference/api-auth/test-token-issuer)
`.Symmetric` mints them.

## SDK Reference

> [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [TestTokenIssuer](/docs/sdk-reference/api-auth/test-token-issuer)
