---
authors: [Theauxm]
areas: [auth, platform]
status: accepted
---

# A gated train with no authorization service refuses to start

If any registered train carries `[TraxAuthorize]` and no `ITrainAuthorizationService` is
registered, the host throws at startup. Opting out is possible and explicit:
`TraxMediatorBuilder.AllowMissingAuthorizationService()`.

The same validator rejects a malformed attribute: a `Policy` that is whitespace, or `Roles`
that parse to nothing.

## Status

**Accepted.**

## Considered options

**Fail open**, treating a missing service as "no authorization configured, allow
everything". This is the default most frameworks pick and it is the reason this ADR exists.
The failure is silent and inverted: the developer who wrote `[TraxAuthorize]` believes the
train is gated, the attribute is present in the source, and every request succeeds. Nothing
in the logs says the gate was never installed.

**Warn and continue.** Rejected for the same reason `core/0001` rejects it for the
analyzer: a warning is not read, and the consequence here is an ungated train serving
traffic.

**No opt-out at all.** Rejected because there is a legitimate case: a host wiring trains for
a test, or a stage of a migration where the attributes land before the service does. Making
that case say so in one call is better than making it impossible, because the alternative is
that somebody deletes the attributes instead.

## Consequences

**The opt-out is a single named call, which is the point.** It appears in `Program.cs`, it
is greppable, and it reads as a decision. A configuration flag or an environment variable
would not.

**A malformed attribute is treated as a misconfiguration, not as an empty policy.**
`[TraxAuthorize(Policy = " ")]` throws rather than quietly gating on nothing, because the
two are indistinguishable at run time and only one of them is what anybody meant.

**The same rule is checked again when a train is run or queued.** `TrainExecutionService`
refuses a `[TraxAuthorize]` train when no `ITrainAuthorizationService` is registered, for hosts
where the startup validator never runs (the Lambda runner, a bare `ServiceProvider`). It has two
exemptions: the opt-out above, and a trusted execution scope, which marks work already
authorized at its own gate (a scheduler pipeline, a remote job runner, the dashboard) and which
an enforcer would skip as well. In a normal hosted app the trusted-scope exemption never comes
into play, because the startup validator has already refused the host unless the opt-out was
taken.

## Exemplars

- `AuthorizationRegistrationValidatorTests` covers both directions: the throw when the
  service is missing, the pass when it is registered or the opt-out is taken, and the
  malformed-attribute cases.

The runtime check and its trusted-scope exemption are exercised by unit tests in the
memory-leak suite, which are not declared guards for this ADR.

Not covered: nothing checks that the registered `ITrainAuthorizationService` actually
authorizes anything. A stub that returns success for every train satisfies this ADR
completely, which is by design, since the service is the consumer's to write.

## Changelog

- **2026-09-23**: Recorded the runtime fail-closed check in `TrainExecutionService`, and that a
  trusted execution scope is a second exemption from it besides the opt-out.
- **2026-09-11**: Recorded.
