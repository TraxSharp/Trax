---
title: "6.3 Roles and a development signing key"
description: "Chapter 6 of the Trax book, section 3: Roles and a development signing key."
type: tutorial
area: general
parent: "6. Customers and staff"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/customers-and-staff/packages
---

# 6.3 Roles and a development signing key

Two roles cover Shopfront's callers. Role names are compared exactly, so they are written once, as
constants.

`Auth/Roles.cs`:

```csharp compile=app file=Auth/Roles.cs
namespace Shopfront.Auth;

public static class Roles
{
    public const string Customer = "Customer";
    public const string Staff = "Staff";
}
```

In production the tokens come from an identity provider. On a developer's machine there is none, so
Shopfront signs its own, with a key that says what it is. Trax refuses to start outside Development
with any key containing `do-not-use-in-production`, so this one can be committed and can never
guard a real deployment.

`Auth/DevJwt.cs`:

```csharp compile=app file=Auth/DevJwt.cs
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Shopfront.Auth;

// Tokens for Development only. The host refuses this key in any other environment.
public static class DevJwt
{
    public const string Issuer = "shopfront-dev";
    public const string Audience = "shopfront";

    public static readonly byte[] Key = Encoding.UTF8.GetBytes(
        "shopfront-dev-signing-key-do-not-use-in-production"
    );

    public static string Mint(string user, string role) =>
        new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = Audience,
                Subject = new ClaimsIdentity(
                    [new Claim("sub", user), new Claim("name", user), new Claim("role", role)]
                ),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Key),
                    SecurityAlgorithms.HmacSha256
                ),
            }
        );
}
```

Trax reads the token's `sub` as the caller's id, `name` as their display name and `role` as their
roles. It qualifies the id with the scheme that signed the caller in, so `ada` signed in by JWT is
`TraxJwt:ada`. Two schemes that both have an `ada` can then never be mistaken for one person.

## SDK Reference

> [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxPrincipal](/docs/security/inject-the-current-principal) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Scope a data context to the caller](/docs/effect/scope-a-data-context-to-the-caller) | [Effect data testing](/docs/sdk-reference/testing/effect-data-testing)

---

Next: [6.4 Who may run PlaceOrder](/docs/book/customers-and-staff/who-may-run-placeorder)
