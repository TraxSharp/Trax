---
layout: default
title: AddTraxJwtDispatcher
parent: API Auth
grand_parent: SDK Reference
---

# AddTraxJwtDispatcher

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Registers a policy authentication scheme that inspects the inbound Bearer token's `iss` claim and forwards authentication to the matching JWT scheme registered by [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth). One endpoint, one `RequireAuthorization` call, several upstream identity providers.

## Signature

```csharp
public static AuthenticationBuilder AddTraxJwtDispatcher(
    this IServiceCollection services,
    Action<JwtDispatcherBuilder> configure);
```

The dispatcher also registers an internal rejection scheme (`JwtDefaults.RejectSchemeName`) that produces a 401 for any request whose issuer does not match a configured mapping. Calling `AddTraxJwtDispatcher` twice with the same scheme name throws `InvalidOperationException`.

## When to reach for it

Use the dispatcher when:

- More than one JWT issuer can authenticate a given endpoint.
- The set of issuers is closed and known at registration time.
- Per-endpoint authorization should not enumerate scheme names.

Skip the dispatcher when only one issuer is in play. The default-scheme overloads of `AddTraxJwtAuth` are simpler.

## Usage

```csharp
services.AddTraxJwtAuth<CognitoResolver>("cognito", jwt =>
    jwt.UseAuthority("https://cognito-idp.us-east-1.amazonaws.com/us-east-1_ABC", "mobile-client"));

services.AddTraxJwtAuth<InternalResolver>("internal", jwt =>
    jwt.UseSymmetricKey("nwyc-web", "nwyc-trax", internalSecretBytes));

services.AddTraxJwtDispatcher(d =>
{
    d.MapIssuer("https://cognito-idp.us-east-1.amazonaws.com/us-east-1_ABC", "cognito");
    d.MapIssuer("nwyc-web", "internal");
});

app.MapGraphQL().RequireAuthorization(JwtDefaults.DispatcherSchemeName + "-JwtPolicy");
```

## `JwtDispatcherBuilder` methods

| Method | Purpose |
|---|---|
| `MapIssuer(string issuer, string schemeName)` | Maps an `iss` value to a registered scheme. Duplicate issuers throw. |
| `WithSchemeName(string schemeName)` | Overrides the dispatcher's own scheme name. Defaults to `JwtDefaults.DispatcherSchemeName`. |
| `FallbackToScheme(string schemeName)` | Forwards tokens with unmapped issuers to the named scheme instead of rejecting. The chosen scheme runs its own issuer validation. |

Issuer comparison is ordinal and trailing-slash sensitive. The mapping value must match the `iss` claim exactly. The dispatcher reads `iss` without validating the signature; the matched scheme then performs full validation (signature, issuer, audience, lifetime).

## What the dispatcher does on each request

1. Reads the `Authorization` header. Missing or non-Bearer → forwards to the fallback scheme (default: reject, 401).
2. Parses the token's `iss` claim without validating its signature. Unparseable → forwards to fallback.
3. Looks up the issuer in the configured map. Hit → forwards to that scheme; miss → forwards to fallback.
4. The chosen scheme runs the full `JwtBearer` validation pipeline.

The dispatcher never authenticates a token itself. It only chooses which validator to run.

## Return semantics

| Condition | Result |
|---|---|
| Bearer header missing | 401 (`AuthenticateResult.Fail` from reject scheme) |
| Bearer present but malformed | 401 (peek returns null, falls through) |
| Bearer present, `iss` unknown | 401 unless `FallbackToScheme` is set |
| Bearer present, `iss` mapped, validation succeeds | `AuthenticateResult.Success` from the matched scheme |
| Bearer present, `iss` mapped, validation fails | 401 from the matched scheme |

## Caveats

- The peek-then-validate dance means an attacker can route their token to any registered scheme by setting `iss`. That scheme's own issuer check is what stops them: never trust the unvalidated peek value beyond routing.
- The dispatcher is HTTP-only. GraphQL subscriptions over WebSockets bypass it and run only the default scheme. See [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) for the subscription caveat in multi-issuer setups.

## SDK Reference

> [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [JwtDefaults](/docs/sdk-reference/api-auth/jwt-defaults) | [TraxAuthClaimTypes](/docs/sdk-reference/api-auth/trax-auth-claim-types)
