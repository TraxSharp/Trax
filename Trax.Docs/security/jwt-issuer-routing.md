---
title: How the JWT dispatcher routes tokens
description: Why AddTraxJwtDispatcher picks a JWT scheme from an unvalidated iss claim, what keeps that safe, and when one scheme or named policies are the simpler choice.
type: explanation
area: security
nav_order: 30
---

# How the JWT dispatcher routes tokens

A host that accepts tokens from more than one issuer registers one JWT scheme per issuer, each with
its own authority or key, audience and resolver. ASP.NET Core then needs to know which scheme
should validate a given request. The dispatcher's members and results are on
[AddTraxJwtDispatcher](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher).

## The problem it solves

Without a dispatcher, an endpoint either names one scheme's policy, and accepts only that issuer,
or names `TraxAuthPolicy`, and every registered scheme tries the token in turn. The first is too
narrow for an endpoint that customers and internal services both call. The second runs validators
that were never going to succeed and makes the failure of interest, the one from the issuer that
did mint the token, one of several.

The dispatcher is a policy scheme: it chooses one scheme per request and forwards to it, so the
endpoint names one policy and exactly one validator runs.

## Why reading `iss` unvalidated is safe

To choose, the dispatcher reads the token's `iss` claim before anything has checked the signature.
A caller can put any issuer there, so the choice is the caller's. That is acceptable because the
choice is only of which validator runs, never of whether validation runs: the chosen scheme checks
the signature against its own keys and the issuer against its own expected value. A forged token
routed to the `customers` scheme still has to be signed by the customers' identity provider.

The peeked value is therefore good for routing and for nothing else. Code that wants the issuer
reads it from the authenticated principal, after the scheme validated it.

## Why an unknown issuer is refused

An issuer with no mapping goes to a scheme that fails every request, not to whichever scheme
happens to be the default. A token from an issuer the host never configured is one it has no keys
for, so refusing it costs nothing, and falling through to a default would let the default's
behaviour decide what an unrecognised token gets. `FallbackToScheme` exists for a host that does
want unmapped issuers tried against one scheme; that scheme still validates the issuer itself.

## When not to use it

With one issuer, the default scheme is simpler: there is nothing to choose. With several issuers
whose endpoints never overlap, a named policy per endpoint (`customers-JwtPolicy`) says the same
thing more directly. The dispatcher earns its place when the set of issuers is closed and known at
startup and one endpoint, or one subscription socket, has to accept all of them.

## SDK Reference

> [AddTraxJwtDispatcher](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher) | [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [JwtDefaults](/docs/sdk-reference/api-auth/jwt-defaults)
