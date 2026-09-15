---
authors: [Theauxm]
areas: [auth, platform]
status: accepted
---

# Trax owns the authorization vocabulary

`[TraxAuthorize]` and `[TraxAllowAnonymous]` are how a consumer declares authorization on any
Trax surface, and they now apply to a method as well as a class or an interface. Trax translates
them into whatever the server underneath needs, currently HotChocolate's `@authorize` directive.
A surface that declares its posture with the server's own attribute instead is refused, by name,
at host startup.

The two rules are the same rule. The attributes exist so that a consumer says what they mean once
and Trax deals with the server, and that only holds while Trax's vocabulary is the complete
answer. The moment there is a surface Trax cannot express, the server's vocabulary becomes the
real one for that surface, and the abstraction is worth less than the dependency it was hiding.

## Status

**Accepted.**

## Considered options

**Leaving the attributes at class and interface targets, and accepting HotChocolate's on a
resolver.** This is what shipped first, and it is what prompted the decision. HotChocolate's
`[Authorize]` already applies to a method, so it was the only thing that compiled on a resolver,
and the cost looked like sequencing: widening the targets here means this repo releases before
`Trax.Api` can enforce the widening, and in that window `[TraxAuthorize]` on a resolver would
compile and do nothing. That window is the ordinary shape of a cross-repo change in this
workspace, and it does not justify making another framework's vocabulary the documented answer
for a whole class of fields.

**A separate method-targeting attribute in `Trax.Api.GraphQL`, deriving from HotChocolate's
`ObjectFieldDescriptorAttribute`.** Cheaper: HotChocolate does the directive work, and this repo
never changes. Rejected because it splits the vocabulary across two names, which is most of the
problem, and because the split would be permanent: the GraphQL-flavoured attribute could never be
used on a train.

**Referencing HotChocolate here so the attribute could apply itself.** That is how HotChocolate's
own attribute works, and it is why it needs no interceptor. Rejected outright. A GraphQL server is
a `Trax.Api` concern, and this assembly sits three repos upstream of it. The foreign attributes
are matched by full name for exactly this reason.

## Consequences

**Refusal is by name, not by type.** This assembly cannot reference HotChocolate, so the banned
attributes are a list of strings. A rename upstream would slip past it until the list is updated.
The alternative was a dependency that would have been much harder to remove later.

**ASP.NET Core's `[Authorize]` is deliberately not banned.** It governs endpoints and MVC actions,
a surface Trax does not own and has no business policing.

**Consumers on the server's attributes have to migrate.** A field carrying one fails at startup
with a message naming the Trax replacement. There is no silent period where both work: accepting
both is how a vocabulary splits.

## Exemplars

- `TraxAuthorizationTests` pins the method targets, what a class and a method declare, the
  de-duplication across inheritance paths, and the refusal, including that a same-named attribute
  in another namespace does not trip it.

**Enforced elsewhere:** `TypeExtensionExposureInterceptor` in Trax.Api reads these attributes off
a resolver, emits the `@authorize` directive from them, and fails the host when a field carries a
foreign attribute instead. `NoForeignAuthorizationAttributesTests` in Trax.Api keeps the banned
attributes out of Trax's own code, with an allowlist for the translation layer that emits the
directive.

Not covered:

- Nothing checks a consumer's code at compile time. The refusal happens when the host starts, so
  a project that never boots a Trax GraphQL host is never told.
- The ban reaches only what Trax can see, which is a field on a type Trax owns. A consumer's own
  `ObjectType` outside the Trax pipeline is not inspected.

## Changelog

- **2026-09-15**: Recorded.
