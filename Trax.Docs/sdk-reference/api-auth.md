---
title: API Auth
description: "Index of the Trax auth reference: the principal abstraction, the API-key, JWT, Cognito and OIDC schemes, Cognito token minting and the JWT test fixtures."
type: landing
area: security
parent: SDK Reference
has_children: true
nav_order: 8
---

# API Auth

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Reference for the Trax authentication packages. How they fit into a secured host is in [API Security](/docs/api-security).

## Trax.Api.Auth

- [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal): the identity record, its claims round trip, `TraxPrincipalId`.
- [AddTraxPrincipalAccessor](/docs/sdk-reference/api-auth/add-trax-principal-accessor): the injectable `TraxPrincipal` and `TraxCaller`.
- [TraxCaller](/docs/sdk-reference/api-auth/trax-caller): the caller context that never throws.
- [ITraxPrincipalResolver](/docs/sdk-reference/api-auth/i-trax-principal-resolver): maps a credential to a principal.
- [TraxAuthClaimTypes](/docs/sdk-reference/api-auth/trax-auth-claim-types): the claim types and the combined policy.

## Trax.Api.Auth.ApiKey

- [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth): the API-key scheme and `ApiKeyBuilder`.
- [HashedApiKeyResolver](/docs/sdk-reference/api-auth/hashed-api-key-resolver): the constant-time resolver over hashed keys.
- [ApiKeyAuthenticationOptions](/docs/sdk-reference/api-auth/api-key-authentication-options): the header the scheme reads.
- [ApiKeyDefaults](/docs/sdk-reference/api-auth/api-key-defaults): scheme, policy and header names.

## Trax.Api.Auth.Jwt

- [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth): the JWT bearer scheme, `JwtBuilder`, `DefaultJwtPrincipalResolver`.
- [JwtProviderExtensions](/docs/sdk-reference/api-auth/jwt-provider-extensions): Google, Auth0, Entra and Cognito shortcuts.
- [AddTraxJwtDispatcher](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher): routes a token to a scheme by its issuer.
- [JwtDefaults](/docs/sdk-reference/api-auth/jwt-defaults): scheme and policy names.

## Trax.Api.Auth.Jwt.Cognito

- [UseCognito](/docs/sdk-reference/api-auth/use-cognito): validation for an Amazon Cognito user pool, and `CognitoJwtPrincipalResolver`.
- [CognitoDefaults](/docs/sdk-reference/api-auth/cognito-defaults): Cognito claim names.

## Trax.Api.Auth.Jwt.Cognito.Issuer

- [CognitoTokenIssuer](/docs/sdk-reference/api-auth/cognito-token-issuer): mints Cognito-shaped access and ID tokens.
- [IRefreshTokenStore](/docs/sdk-reference/api-auth/i-refresh-token-store): refresh tokens with rotation chains, and the in-memory store.

## Trax.Api.Auth.Jwt.Testing

- [TestJwksServer](/docs/sdk-reference/api-auth/test-jwks-server): an in-process discovery and JWKS host.
- [TestTokenIssuer](/docs/sdk-reference/api-auth/test-token-issuer): mints test tokens, with `TestTokenBuilder`.

## Trax.Api.Auth.Oidc

- [AddTraxOidcAuth](/docs/sdk-reference/api-auth/add-trax-oidc-auth): the OpenID Connect code-flow scheme, `OidcBuilder`, `DefaultOidcPrincipalResolver`.
- [OidcDefaults](/docs/sdk-reference/api-auth/oidc-defaults): scheme, cookie, policy and path names.
