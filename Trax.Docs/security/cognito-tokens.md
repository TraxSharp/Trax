---
title: Cognito tokens in Trax
description: How Amazon Cognito's ID and access tokens differ, how federated sign-ins reach a Trax host, and how a local Cognito stand-in fits alongside the real user pool.
type: explanation
area: security
nav_order: 31
---

# Cognito tokens in Trax

Amazon Cognito user pools issue tokens that look like any other JWT but do not follow the usual
conventions for audience and token type, which is why Trax ships a Cognito helper rather than
pointing `AddTraxJwtAuth` at the pool and stopping there. The helper's members are on
[UseCognito](/docs/sdk-reference/api-auth/use-cognito).

## ID tokens and access tokens

Cognito issues two tokens per session. An ID token carries who the user is (`email`, `name`, the
federated `identities`) and puts the app client id in `aud`, as OIDC expects. An access token
carries OAuth scopes, puts the app client id in a `client_id` claim instead, and has no `aud` at
all. A standard JWT validator checks `aud`, so it accepts the ID token and refuses the access
token, while many client SDKs send the access token by default.

`UseCognito` replaces the audience check with one that knows both shapes, and checks the
`token_use` claim so a host that asked for only one shape does not accept the other. It also
refuses a token that claims the access shape (`client_id`) without `token_use`: Cognito always
stamps `token_use` on an access token, so a token without it is not one, and the `client_id` claim
alone vouches for nothing. That rule is scoped to tokens carrying `client_id`, so an internal
issuer's tokens on the same host, which carry neither claim, are unaffected.

Which shape to accept is the host's choice: both, when different clients send different tokens;
access only, when the clients send access tokens and nothing reads profile claims; ID only, when
the host needs the user's attributes.

## Federation

A user pool can broker Google, Apple, SAML and other OIDC providers. The tokens still come from the
pool: the pool is the issuer, `sub` is the Cognito-assigned UUID, and an `identities` claim, a JSON
array, describes the upstream provider. So the Trax side does not change with federation: it
validates one issuer and identifies users by the pool's `sub`. `CognitoJwtPrincipalResolver`
surfaces the provider as an `identity_provider` claim for code that wants to know how the user
signed in.

A user who signs in through two providers is two pool users unless the pool links them, which is
Cognito configuration (typically a `PreSignUp` trigger that calls `AdminLinkProviderForUser`), not
Trax's.

## Local development and tests

A Cognito user pool cannot run locally, so `Trax.Api.Auth.Jwt.Cognito.Issuer` mints tokens with the
same claims a pool emits. A local stand-in service, or a test, signs tokens with it and serves its
key, and the host keeps the same `UseCognito` validation for real and local tokens. The package
covers tokens and refresh-token rotation only; sign-up, hosted UI, MFA and triggers are the pool's
own API, which a stand-in builds itself if it needs them. Minting is on
[CognitoTokenIssuer](/docs/sdk-reference/api-auth/cognito-token-issuer), and a test walkthrough on
[Test JWT authentication](/docs/security/test-jwt-authentication).

## SDK Reference

> [UseCognito](/docs/sdk-reference/api-auth/use-cognito) | [CognitoDefaults](/docs/sdk-reference/api-auth/cognito-defaults) | [CognitoTokenIssuer](/docs/sdk-reference/api-auth/cognito-token-issuer)
