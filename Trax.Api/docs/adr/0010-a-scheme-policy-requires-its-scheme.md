---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# A scheme policy requires its scheme

Each policy an auth package registers for its scheme (`ApiKeyPolicy`, `JwtPolicy`,
`{scheme}-JwtPolicy`, the dispatcher's policy, `OidcPolicy`) carries `TraxSchemeRequirement`,
which is satisfied only by an identity that scheme authenticated. The policy then means "a caller
this scheme authenticated" wherever it is evaluated: the endpoint gate, `GateOperations(policy)`,
`[TraxAuthorize(Policy = ...)]`, and a socket.

A GraphQL HTTP request is authenticated by one Trax interceptor. With an endpoint policy it
authenticates with that policy's own schemes, as ASP.NET Core does for an endpoint gated by the
same policy; without one it tries every registered scheme when nothing upstream authenticated
the request.

## Status

**Accepted.**

## Why this is written down

A policy's authentication schemes look like part of the rule, and they are not.
`IAuthorizationService.AuthorizeAsync(user, policy)` evaluates the policy's requirements against
the principal it is handed and ignores its schemes, which only tell ASP.NET Core's policy
evaluator which handlers to run before authorizing. A scheme policy needs a requirement of its
own to mean what its name says.

## Considered options

**Relying on the schemes list.** It works for an ASP.NET Core endpoint gated with
`RequireAuthorization(policy)`, because the policy evaluator authenticates with those schemes
first. It does not work for HotChocolate's `@authorize`, for a socket, or for anything else
that calls the authorization service directly.

**A claim naming the scheme.** Trax already builds every identity with the scheme's name as its
authentication type, on HTTP and on sockets, so a new claim would be a second record of the same
fact. The requirement compares the authentication type. The OIDC session identity is built under
the OIDC scheme's name and carried by the cookie scheme, so `OidcPolicy` accepts either name.

**Two HTTP interceptors, one to authenticate and one to check the endpoint policy.** HotChocolate
keeps one `IHttpRequestInterceptor` per schema, so two would replace each other. One interceptor
does both.

## Consequences

**The combined `TraxAuthPolicy` is unchanged.** It means "any Trax scheme authenticated the
caller" and does not claim to be scheme-specific.

**A host policy is untouched.** Only the policies Trax registers carry the requirement.

## Exemplars

- `SchemePolicyRequiresItsSchemeTests` pins each scheme policy against principals from its own
  and other schemes, the dispatcher's policy against a mapped scheme and another, and over HTTP
  on a host with an API-key and a JWT scheme and no default one: the combined endpoint gate
  admits an API-key caller and a JWT caller and refuses an anonymous one, an endpoint or the
  operations namespace gated by `ApiKeyPolicy` refuses a JWT caller and admits an API-key one.
- `TraxHttpAuthenticationInterceptorTests` pins authentication without an endpoint policy: an
  already authenticated request is left alone, the first scheme that authenticates wins, and a
  request no scheme authenticates stays anonymous.

Not covered: a host that builds its own identities under a Trax scheme's name satisfies that
scheme's policy. That is the host's code and Trax cannot tell it apart.

## Changelog

- **2026-09-27**: Recorded.
