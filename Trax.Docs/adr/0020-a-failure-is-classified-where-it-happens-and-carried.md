---
authors: [Theauxm]
repos: [core, effect, scheduler, api, dashboard]
areas: [platform, data-model]
status: accepted
---

# A failure is classified where it happens, and the answer is carried

A failed run records a `FailureClass` (Unclassified, Transient, Conflict, Permanent) decided by a
consumer's `IFailureClassifier` in the process that holds the real exception. A class decided
there travels with the failure, on `TrainExceptionData` and across the remote-run wire, and
wins over anything the calling side would derive. Trax supplies the vocabulary; nothing in Trax
acts on it yet.

## Status

**Accepted.**

## Considered options

**Classifying on the calling side.** Rejected because a remote failure arrives as a rebuilt
exception whose type is gone, so the calling side could only match on a type name or a message,
which is what classification exists to avoid.

**A free-form string.** Rejected in favour of a closed vocabulary Trax owns (docs/0013), so that
retry and dashboard logic can be written against it. It lives in Trax.Core because it rides on
`TrainExceptionData`, which Core defines.

**Acting on the class.** Deferred deliberately: retry stays count-based, so registering a
classifier changes what is recorded and nothing else.

## Consequences

The vocabulary is stored as the Postgres enum `trax.failure_class` and sent over the wire as its
integer value, so adding a class needs a migration and a release of every reader before any
writer uses it (effect/0006). Failures the scheduler records itself, a dispatch failure or a run
the reaper fails, are Unclassified, and a client-side timeout that surfaces as a cancellation is
never classified.

A failure rebuilt from a serialized record, which is how a remote failure reaches the calling
side, is never passed to the local classifier. If the worker sent a class it is recorded; if it
sent none the run stays Unclassified, because re-deriving one from the rebuilt exception is the
option rejected above. Trax writes the class on the wire as an integer where it controls the
serializer (the job-runner endpoint, the Lambda runner's local HTTP route); a Lambda
invocation's response goes through the function's own serializer, so both executors accept an
integer or a name, and a class they do not know reads as Unclassified with the worker's error kept
(scheduler/0001).

A failure raised outside any junction has its class attached to the exception too: when the
classifier answers and the exception carries no `TrainExceptionData`, it is created, so a remote
worker reports that class instead of none.

## Exemplars

**Enforced elsewhere:** `FailureClassificationTests` in Trax.Effect (recording, failures outside a
junction, a carried class winning, a throwing classifier, and a rebuilt remote failure with no
class staying Unclassified), `JunctionFailureClassTests` in
Trax.Core (a junction keeps a class the failure carried), and `RemoteFailureClassificationTests`
and `LambdaRunExecutorTests` in Trax.Scheduler (the class crossing HTTP and Lambda, and an older
worker that sends none).

Not covered: nothing checks that a host registers its classifier in the worker process, which is
where remote runs are classified.

## Changelog

- **2026-09-23**: Recorded.
