---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples]
areas: [testing]
status: accepted
---

# A test owns every timeout it waits behind

A test that asserts under a deadline also sets the component timeouts that can delay what it
is waiting for, to values shorter than that deadline. Where
[0006](./0006-tests-synchronise-on-a-signal.md) says a wait must be on a signal rather than a
sleep, this says the ceiling on that wait has to be one the test chose, not one it inherited
from a framework default it never looked up.

## Status

**Accepted.**

## Why this is written down

Trax.Api release CI failed twice on `CognitoJwksToken_Acks`, the one socket test whose
`connection_ack` depends on an HTTP fetch, because the subscription interceptor reads the
scheme's OIDC discovery document to get JWKS keys. The test gave the receive a 10 second
ceiling and nothing gave the fetch one, so the fetch ran on the JwtBearer default of 60
seconds. The test then spent 1 minute 12 seconds to report an `OperationCanceledException`
with nothing but the test helper in the stack. The same test takes 143 milliseconds when
healthy, in a full local run of the same suite, so the ceiling was never the problem, and
reading the failure as a tight budget would have sent the next person to raise it.

## Considered options

**Raise the deadline past the inner timeout.** The reflex, and it loses at both ends. Every
genuine failure in that test now pays the longer wait, and the report is still a bare
cancellation rather than the error underneath it. It is also not a closed set: the deadline
has to clear the longest default among every layer the wait passes through, and nothing
enumerates those for you.

**Retry the flaky test.** [0006](./0006-tests-synchronise-on-a-signal.md) refused this
already, for the reason that holds here too. It teaches the suite to tolerate a stall rather
than describe it.

**Stub the slow dependency out.** Sometimes the right answer, but it retires the coverage. A
test written because a code path really does call out to an identity provider stops covering
that path once the call is gone.

## Consequences

**Failures name their cause.** An inner timeout that fires first surfaces the rejection or
the transport error. A deadline that fires first surfaces only that time ran out.

**The production default stops being exercised, so it has to be chosen.** This is the real
cost, and it cuts both ways. Trax.Api sets no `BackchannelTimeout` on any JWT scheme, so a
slow identity provider can stall every `connection_init` on a JWKS-backed scheme for a
minute, and the one test that would have felt it is now the test configured not to. A
timeout a test overrides is a timeout somebody owes an answer for in the shipped code.

**It applies to any inherited ceiling, not only HTTP.** Connection and command timeouts,
broker acknowledgement windows and host shutdown timeouts all sit behind ordinary awaits,
and all carry defaults measured in tens of seconds.

## Exemplars

- [Test Conventions](/docs/reference/test-conventions) is the rule this produces.

**Unenforced:** the timeouts a wait sits behind are not visible where the wait is written.
They come from whichever services the host was composed with and from framework defaults the
test never names, so no check over test source can enumerate them and compare them against
the deadline.

## Changelog

- **2026-09-16**: Recorded.
