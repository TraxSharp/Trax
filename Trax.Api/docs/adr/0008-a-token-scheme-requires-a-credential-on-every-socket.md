---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# A token scheme requires a credential on every socket

When a host registers an API-key or JWT scheme, `TraxCompositeSocketInterceptor` requires every
subscription connection to present a credential for it in `connection_init`. An upgrade request
that is already authenticated, typically by a session cookie from a cookie or OIDC scheme on the
same host, does not satisfy that requirement: the connection is rejected like any other
connection without a credential. Only a host with no token scheme accepts a connection on the
strength of the upgrade alone.

## Status

**Accepted.** Decided on 2026-09-27, when accepting the authenticated upgrade was proposed and
declined.

## Why this is written down

Because the rejected option looks like a convenience with no cost. A host running both a cookie
scheme for its browser app and a token scheme for other clients sees browser subscriptions
refused although the upgrade request carried a valid session, and the obvious fix is to let the
composite accept an upgrade whose `HttpContext.User` is already authenticated.

## Considered options

**Accept an already-authenticated upgrade (rejected).** A cookie on a socket upgrade is ambient
authority: the browser attaches it to any upgrade to the host, whichever page opened the socket,
and the socket then carries that identity for its whole lifetime. A payload credential is
explicit: the page has to hold it and choose to send it. Registering a token scheme is the host
saying subscriptions are authenticated by an explicit credential, and accepting the cookie in its
place would quietly turn that into "or by whatever identity the browser happens to attach". It
would also make the protection a host gets from checking where an upgrade comes from the only
protection left on these sockets, instead of one of two.

**Require the credential (chosen).** Stricter for mixed hosts, and the cost is visible and small:
a browser app on such a host sends its token in `connection_init`, as a non-browser client already
does. A host that wants cookie-only subscriptions registers no token scheme, and the composite
then accepts on the upgrade.

**Make it a per-host option.** Rejected for now. It would put the weaker rule one line away on
every mixed host, and nobody has needed it. It can be added later without undoing this.

## Consequences

**A host with both a cookie scheme and a token scheme authenticates browser subscriptions with a
token.** The docs for subscriptions say so, and the rejection message names the missing
credential.

## Exemplars

- `AuthenticatedUpgradeRequiresCredentialTests` pins it: an upgrade whose `HttpContext.User` is
  authenticated and whose payload is empty is rejected with an API-key scheme registered, and
  with API-key and JWT registered together, while the same upgrade presenting a valid key is
  accepted.

Not covered: nothing stops a host from supplying its own socket interceptor that accepts the
upgrade's identity. That replaces Trax's interceptor, and the decision is then the host's.

## Changelog

- **2026-09-27**: Recorded.
