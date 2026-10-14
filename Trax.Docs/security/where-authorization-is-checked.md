---
title: Where Trax checks authorization
description: "Why Trax uses its own attributes, refuses a host with an undeclared surface, checks a train once at submission and trusts the scheduler and dashboard."
type: explanation
area: security
nav_order: 1
---

# Where Trax checks authorization

Trax authorizes GraphQL work in a few places and deliberately not in others. The rules themselves
are on [Authorization rules](/docs/authorization); this page is about why they are shaped the way
they are, and what each choice costs.

## Nothing is public by accident

A GraphQL server built the usual way is open until someone adds a gate. A new mutation, a new
entity in a query model, a resolver bolted onto an existing type: each ships public unless its
author remembered. Trax inverts that. Every surface the endpoint exposes states its posture, gated
with `[TraxAuthorize]` or public with `[TraxAllowAnonymous]`, and a host where one does not is
refused at startup with a message naming it. The cost is one attribute per surface and a host that
will not start until it is written. The benefit is that "public" is something a reviewer can see in
a diff.

The same reasoning covers the places a surface can appear without its author thinking of it as a
surface: an entity a query model reaches through a navigation, which HotChocolate puts in the
schema with its own filter and sort inputs; a field a type extension adds to a public type or a
root; a `[TraxBroadcast]` train, whose runs stream to subscribers. Each answers the same question.
The `operations` namespace, which reads execution inputs and drives the scheduler, has no
anonymous default at all: exposing it means choosing a gate, a whole-endpoint gate, or an explicit
`AllowAnonymousOperations()`.

Everything that can be decided before the first request is decided then. A policy name that is
not registered would turn a gate into a silent deny-all, so it refuses the host instead. A demo key
in a published sample would authenticate anyone who read the sample, so outside Development it
refuses the host too. [Startup refusals](/docs/security/startup-refusals) lists them.

## Trax owns the vocabulary

HotChocolate has `[Authorize]` and `[AllowAnonymous]` of its own, and Trax refuses them on the
fields it checks. This is not about naming. The same question answered by two attributes would
have two sets of combinator rules and two inheritance behaviours, and code written against one
would be coupled to the GraphQL server underneath, which Trax exists to keep out of a consumer's
domain code. `[TraxAuthorize]` works on a train, which never reaches the schema as a type and on
which HotChocolate's attribute means nothing; it works on an entity, an interface and a resolver.

The difference that bites is placement. HotChocolate applies a class-level attribute on an
`[ExtendObjectType]` class to the type being extended: on a public entity it re-locks the whole
entity, and on a root type it sets the posture of every operation in the schema. `[TraxAuthorize]`
on an extension class applies to the fields that extension contributes, which is what someone
writing it there means. ASP.NET Core's `[Authorize]` is a different surface, endpoints and MVC
actions, and is left alone. The decision is recorded in `Trax.Docs/adr/0013`.

Combining follows ASP.NET Core's rule for repeated `[Authorize]` attributes: each attribute is a
requirement and all must pass, so adding an attribute can only narrow who gets in. Earlier
versions merged role lists across attributes into one any-of list, so a method-level role widened
a class-level one (`Trax.Api/docs/adr/0030`). Roles used to be compared case-insensitively after
upper-casing both sides, which let a culture's case mapping equate characters a reader sees as
different; they now match exactly, as `@authorize` and `RequireRole` do (`Trax.Docs/adr/0026`).

## Two enforcement points

A train is checked imperatively. `ITrainExecutionService` calls the enforcer before it reads the
input, so a caller who may not run a train learns nothing about the input it expects, not even
from a parse error. A query model is checked declaratively: Trax translates `[TraxAuthorize]` into
HotChocolate's `@authorize` directive on the object type and on the entry field. It has to be both.
The field directive alone would leave the type readable through a navigation from an ungated
parent; the type directive alone would leave `totalCount` and `pageInfo` answering for a caller who
may not read a single row. Filtering and sorting through a navigation reveal as much as selecting
it, and an input field cannot carry a directive, so Trax evaluates the target type's directives
itself before such a query runs.

The endpoint gate, `RequireAuthorization()` on the builder, is a third and coarser layer: one
policy for every operation. It is not redundant with the per-surface attributes. It answers "may
this caller reach the API at all", they answer "may this caller run this train or read this type",
and both run. Trax counts only the builder's gate in its startup checks, because it is the one it
can see; an ASP.NET Core convention on the mapped route still works, enforced before HotChocolate
runs, but is invisible to the census.

## Checked once, at submission

Authorization is enforced when work is submitted, where there is a request and a caller. A run or
enqueue that passes is the caller's, and what it puts on the work queue is already authorized.
Everything downstream is trusted infrastructure:

- The scheduler dequeues and calls `ITrainBus.RunAsync` directly, which never reaches the enforcer.
- A remote worker runs what the scheduler sends it. A queued job goes through the job runner and
  `ITrainBus`; a remote run (`UseRemoteRun`) goes through `ITrainExecutionService` inside a trusted
  scope (`"scheduler.remote-run"`), so the enforcer skips it.
- A system-owned state machine instance starts its trains inside a trusted scope, as a scheduled
  manifest would. A user-owned instance's trains are authorized against that user.

So a train can carry `[TraxAuthorize("Admin")]` and still be scheduled with `AddScheduler()` or run
on a remote worker. Re-checking it there would need a caller who is not present: a scheduled run
has no request, and the person who defined the manifest may have left the company.

Trust is explicit, never inferred. A missing `HttpContext` is not trust: outside a trusted scope,
the default enforcer refuses a gated train that has no request ("No request context and no trusted
execution scope."), so a background service or a test that calls the execution service directly
cannot slip past by accident. A trusted scope is opened in code, with `ITrustedExecutionScope`,
around work that was authorized at a gate of its own, and its reason is logged when it skips a
check. A custom enforcer is called inside trusted scopes too and decides itself what trust means;
it should key on `IsTrusted`, not on whether a request is present.

Because a remote worker trusts what it is sent, its own endpoints are where that trust is guarded.
Every runner entry point refuses to start without an authorization posture: a signing key shared
with the scheduler, a policy that admits only the scheduler, or an explicit, logged
`AllowUnsignedRequests()`. See [Remote execution](/docs/scheduler/remote-execution). Inside a
remote run `TraxCaller.IsTrusted` is true, so anything keyed on it, row-level filters included,
treats the request as the scheduler's.

## The operations surface and the dashboard

The admin surface enqueues work in two ways, and they are authorized differently
(`Trax.Docs/adr/0017`).

When the caller chooses the train and the input (`workQueue.queueTrain`, `workQueue.runTrain`,
`requeueExecution`, `resumeExecution`), the train's own `[TraxAuthorize]` applies on top of the
operations gate. Passing the operations gate makes someone an operator, not a holder of every
train's role.

When a manifest chooses them (triggering a manifest or a group, requeueing a dead letter), only the
operations gate applies. The train and input were fixed when the manifest was defined, so
triggering it is operating the scheduler rather than submitting new work. The whole trigger and
dead-letter surface sits behind one admin gate, and an operator who passes it can trigger any
manifest. Protect it accordingly.

The dashboard is the admin surface as a whole, gated by the host that serves it. Its dialogs run
inside a trusted scope (`"dashboard"`), so a train's `[TraxAuthorize]` does not apply to a
dashboard user; the train's `OnQueue` hook and the input cap still do. Part of the reason is
mechanical: a Blazor Server circuit has no HTTP request behind a click, and the default enforcer
reads the user from the request. Part is the model: whoever passes the dashboard's gate is an
operator. A dashboard-only or scheduler-only host still has to register an enforcer or call
`AllowMissingAuthorizationService()`, because the startup check that refuses a host with gated
trains and no enforcer does not know which callers it will have.

A run differs from a queue in one way that matters. A queued entry takes its train's
`QueueSubjectKey` and is serialized against other work for the same subject; a run started from
the Run dialog or `workQueue.runTrain` is handed straight to a job submitter and is not
(`Trax.Docs/adr/0019`). The dashboard warns about this; outside a trusted scope a subject-keyed
train cannot be run this way at all and must be queued.

## What a gate does not decide

`[TraxAuthorize]` gates a train or a type. It does not gate a record. A caller who may run a train
supplies its input, and the input is what `QueueSubjectKey` turns into a subject key, so the caller
also chooses which subject the entry serializes against, and the priority it queues at. Subject
keys are one space across all trains. Where that matters, authorize the record in `OnQueue`, which
runs with the caller's identity available, or scope keys per tenant so a key a caller can name is
one they own.

Rows are the same. A gated entity with no row filter serves every user's rows to any caller who
passes the gate. Trax's authorization does not see EF Core query filters, which is why the
owner-scope census in `Trax.Effect.Data.Testing` exists.
