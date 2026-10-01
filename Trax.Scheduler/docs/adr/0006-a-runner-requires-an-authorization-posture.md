---
authors: [Theauxm]
areas: [scheduling, platform]
status: accepted
---

# A runner requires an authorization posture and runs only registered trains

A runner (`UseTraxJobRunner`, `UseTraxRunEndpoint`, `SqsJobRunnerHandler`, `TraxLambdaFunction`)
runs what it is sent as trusted infrastructure: the scheduler already authorized the work, so the
runner opens a trusted execution scope and skips per-train authorization. Every runner entry point
therefore refuses to start until the host says who may send it work, and a runner only ever
deserializes into the input type of a train it has registered.

## Status

**Accepted.**

## The posture

`AddTraxJobRunner(runner => ...)` configures one of three, checked when the entry point starts
(mapping the endpoint, building the Lambda function's services, handling the first SQS batch):

| Posture | Where it applies | What the runner does |
| --- | --- | --- |
| `SigningKey` | every entry point | verifies a `Trax-Signature` over the exact body before reading it |
| `AuthorizationPolicy` | the two ASP.NET endpoints | applies the named policy with `RequireAuthorization` |
| `AllowUnsignedRequests()` | every entry point | accepts anything, and logs a warning naming the entry point at startup |

The signing key is the default recommendation, and the only posture that works on every transport.
A policy cannot apply to an SQS message or a direct Lambda invocation, so a runner configured with
only a policy refuses to start there rather than falling back to accepting everything.

`AddTraxJobRunner()` with no argument still exists, because a host can need `ITraxScheduler`
without mapping a runner (the GameServer API sample does). It registers no posture, so mapping an
endpoint afterwards fails at startup with a message naming the three choices.

**`TraxLambdaFunction`'s local routes take their own posture.** `RunLocalAsync` serves the
function over HTTP for development, on whatever address the host listens on. A signing key
applies there as everywhere. `AllowUnsignedRequests()` is a posture for the Lambda invocation
entry point, which only callers the function's IAM policy admits can reach, so over HTTP it is
honoured only for a caller on the loopback address; a request from any other address is refused
with 401. The smaller of the two ways considered: the other was a separate opt-in for unsigned
local routes, which adds an option whose only safe value is off. A developer who calls the local
routes from another machine or a container gives the function a signing key.

**`ITraxRequestHandler` verifies nothing.** It is the step behind the entry points, and runs what
it is handed in a trusted scope. It stays public, because the separately shipped
Trax.Runner.Lambda and Trax.Scheduler.Sqs call it and a test swaps it for a fake, but it is hidden
from IntelliSense and its documentation says a caller must have verified the request first. The
alternative, a verification result only the verifier can create and the handler requires, changes
the signature every entry point and every fake implements, for a type no host calls directly.

## Reading an HTTP request

The two ASP.NET endpoints and the local Lambda routes read a request the same way, through
`RunnerRequestVerifier.ReadVerifiedBodyAsync`. With a signing key, the `Trax-Signature` header is
checked first: missing, malformed or, for a request that must be fresh, stale is a 401 before any
of the body is read. The body is then read up to `TraxJobRunnerOptions.MaxRequestBodyBytes`
(default 8 MiB; 413 past it, whether or not the request declared its length), verified, and only
then parsed. A refused request is logged as a warning at most once a minute, with the number
refused since the last one, and at Debug otherwise.

## The signature

`v1,t=<unix seconds>,n=<128-bit hex nonce>,s=<base64 HMAC-SHA256>`, keyed with a secret of at least
32 bytes that the scheduler (`SigningKey` on `RemoteWorkerOptions`, `RemoteRunOptions`,
`LambdaWorkerOptions`, `LambdaRunOptions`, `SqsWorkerOptions`) and the runner share. The MAC covers
a version, the purpose (`execute` or `run`), the timestamp, the nonce and the body bytes, so a
signature for one endpoint does not verify on the other and a body cannot be changed under it. It
travels in the `Trax-Signature` HTTP header, the `Signature` property of a `LambdaEnvelope`, or an
SQS message attribute of the same name. None of these touch the JSON body, so
[0001](./0001-remote-execution-is-a-json-wire-contract.md)'s contract is unchanged; a runner without
a key ignores the header, and an older runner ignores the envelope property.

**Freshness depends on the transport.** An HTTP request and a synchronous Lambda `Run` must carry a
timestamp within `MaxClockSkew` (five minutes by default) and a nonce the runner has not accepted
in that window. SQS and an asynchronous Lambda `Execute` are checked for the MAC only, because
both transports redeliver the same message by design, and refusing the redelivery would turn a
retry into a lost job. What stops a redelivered `Execute` running twice is the job's metadata row:
`JobRunnerTrain` runs only a `Pending` row, and only with the input of the train the row names.

The scheduler signs each HTTP retry afresh, so a retry is never refused as a replay of the attempt
before it.

## Only registered trains

A queued job's request names its input type. The runner looks that name up among the input types
of its registered trains and refuses anything else; it never loads a type by the name it was sent.
`LoadMetadataJunction` then resolves the registered train the row names and refuses the row when
there is none, or when that train does not take the input, before anything touches the row, so the
row stays `Pending`. The train the row names is the one that runs (`ITrainBus.RunByNameAsync`),
since two trains may take the same input type. On the scheduler side, a remote run's
output is read into the output type the caller expects, not a type the response names. When that
type is an interface or abstract, and so cannot be read into, the response's type name picks an
implementation of the expected type: only a concrete type already loaded in the scheduler's process
that implements it, matched by name. Nothing is loaded by the name a response gives, and a name that
matches no such implementation is refused.

Registered does not include the scheduler's own trains (`AdminTrains`: the ManifestManager, the
JobDispatcher, the JobRunner and the two cleanup trains). They are registered on any host that
also runs the scheduler, but the scheduler starts them itself, in its own process, and never sends
one to a runner. The run path refuses a train name that is, or could resolve to, one of them, and
`LoadMetadataJunction` refuses a job whose row names one, leaving the row `Pending`. A host
train that shares a scheduler train's short name still runs by that name.

The scheduler's own stored names follow the same rule. The dispatcher resolves a work queue row's
input type, and `LocalWorkerService` a background job's, among the registered trains' input types
rather than by loading the name, so no path that turns a name back into a train input loads a type
by it. The `TypeResolver` that loaded one is deleted.

## What a runner sends back

A `TrainException`'s message is Trax's own account of a failure (see central `docs/0020`) and still
travels. Any other exception is reported by its type alone, with a fixed message, and no stack trace
leaves the runner; the train's metadata row and the runner's log hold the detail.

## Alternatives

**Leave authentication to the host**, as before. It was documented, and it meant a runner mapped by
copying the docs accepted work from anyone who could reach it. A runner is the one place a request
skips train authorization, so an unconfigured one is the wrong default.

**Check the host's own conventions at request time**, accepting `.RequireAuthorization()` chained
onto the returned builder. The conventions are applied after the mapping call returns, so the check
could only run lazily, on the first request, not at startup. A bare `.RequireAuthorization()` also
admits any authenticated principal of the host's schemes, which on a host that shares the API's user
JWT scheme is every end user. Naming the policy in the runner options makes it visible and makes the
startup check possible.

**Mutual TLS or a network boundary.** Both are good and neither is portable across HTTP, SQS and
Lambda. A host that has one uses `AllowUnsignedRequests()` and says so in its log.

**A shared nonce store.** Left out when this was recorded, with the nonce memory per process.
[0009](./0009-a-runner-shares-its-accepted-nonces-through-the-database.md) has since put the nonces
in the Trax database by default, shared by every instance of a runner.

## Consequences

**Upgrading a runner is a breaking deployment step.** A runner that mapped the endpoints with no
posture fails to start on the release that carries this, until the host picks one. That is the
intent: the failure is at startup, with the fix in the message, rather than a silent change in who
can run trains.

**The key has to reach both processes.** It is a secret like any other; rotating it means deploying
the runner and the scheduler with the new key together, or accepting failed dispatches in between.

**Clocks matter.** A scheduler and runner more than `MaxClockSkew` apart refuse every synchronous
request as stale.

## Exemplars

- `RunnerRequestSigningTests` covers the signature, the verdicts (missing, invalid, wrong purpose,
  stale, replayed), the posture check and its startup warning, and the HTTP senders signing each
  attempt.
- `JobRunnerEndpointTests` maps the endpoints with no runner, with no posture, with a policy and
  with a key, and checks the refusals and that no message or stack trace is returned. It also
  pins the reading order: a missing, malformed or stale signature is a 401 with a body stream
  that throws if read, a body over `MaxRequestBodyBytes` is a 413 with or without a declared
  length, and repeated refusals are one warning.
- `JobRunnerTrainTests` runs a `Pending` row with another train's input and asserts the refusal
  leaves the row `Pending`.
- `RunnerRefusesSchedulerTrainsTests` sends each scheduler train's full and short name to the run
  path and a `Pending` row of one to the job runner, and asserts nothing ran and the row stayed
  `Pending`; `TraxRequestHandlerTests` checks a host train sharing a short name still runs.
- `StoredInputTypeResolutionTests` gives the dispatcher and the local worker a stored input type
  that no train takes and asserts nothing is constructed from it; `RegisteredInputTypesTests`
  covers the name matching itself.
- `RemoteRunOutputTests` pins which type a remote run's output is read into: the expected type when
  it is concrete, otherwise only a loaded implementation of it that the response names. Both run
  executors read through it, and `HttpRunExecutorTests.cs` and `LambdaRunExecutorTests.cs` cover the
  same cases end to end.
- `SqsJobRunnerHandlerTests` and `TraxLambdaFunctionTests` cover the same posture on the SQS and
  Lambda paths, including a redelivered `Execute` running again and a repeated `Run` refused.
  `TraxLambdaFunctionTests` also pins the local routes: unsigned from another address refused,
  from loopback served, a signed request served from anywhere, the header checked before the body
  and the size limit.
- [Remote Execution](/docs/scheduler/remote-execution) is the rule this produces.

Not covered: nothing checks that a host does not call `ITraxRequestHandler` from an entry point
of its own. Sharing nonces across runner instances is
[0009](./0009-a-runner-shares-its-accepted-nonces-through-the-database.md)'s, and its guards are named there.

## Changelog

- **2026-09-30**: The HTTP entry points check the signature header before reading the body, hold
  the body to `MaxRequestBodyBytes`, and log refusals as one warning a minute. The local Lambda
  routes honour an unsigned posture only for loopback callers. `ITraxRequestHandler` is hidden
  and documented as verifying nothing.
- **2026-09-30**: The unused `TypeResolver` is deleted.
- **2026-09-30**: The job path resolves and runs the train the row names, not the train registered
  for the input type.
- **2026-09-27**: The per-process nonce memory is replaced by a shared store, recorded in
  [0009](./0009-a-runner-shares-its-accepted-nonces-through-the-database.md).
- **2026-09-27**: A runner refuses the scheduler's own trains on the run path and the job path.
- **2026-09-27**: The dispatcher and the local worker resolve stored input type names among the
  registered trains' inputs too.
- **2026-09-27**: An interface or abstract expected output is read into an implementation of the
  expected type the response names, matched among loaded types; the rule was "never a type the
  response names", which left such outputs unreadable.
- **2026-09-27**: Recorded, with the change it describes.
