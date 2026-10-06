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
the reaper fails, are Unclassified.

A cancellation is a cancellation only when something asked for it: the run's own token was
cancelled, or its persisted cancel flag was set. Any other `OperationCanceledException`, a
client-side `HttpClient` timeout being the usual one, is a failure: it is classified like one,
and recorded Transient when the classifier has no answer. The alternative, recording every
cancellation as Cancelled, meant a manifest never retried a run that timed out against a slow
dependency, because retry counts only Failed runs. The cost is that code which throws its own
`OperationCanceledException` to abandon work now records a failure, and is retried, unless it
cancels the run's token instead.

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

A class is carried in an exception's message only on a `TrainException`, the type Trax rebuilds a
serialized failure as. Any other exception's message is its own text and is never read as a
record. A carried value outside the vocabulary is carried as Unclassified, the same answer the
remote wire gives.

## Exemplars

**Enforced elsewhere:** `FailureClassificationTests` in Trax.Effect (recording, failures outside a
junction, a carried class winning, a throwing classifier, a rebuilt remote failure with no
class staying Unclassified, a class in another exception type's message not being recorded, and
an undefined carried value recording Unclassified), `ModelCoverageGapTests` in Trax.Effect
(`Metadata.AddException` on each of those shapes), `JunctionFailureClassTests` in
Trax.Core (a junction keeps a class the failure carried, reads one from a message only on a
`TrainException`, and carries an undefined value as Unclassified), `UnrequestedCancellationTests`
in Trax.Effect (a cancellation the token or the persisted flag requested stays Cancelled, and one
nothing requested, including a real `HttpClient` timeout, records Failed and Transient), and
`RemoteFailureClassificationTests` and `LambdaRunExecutorTests` in Trax.Scheduler (the class crossing HTTP and Lambda, and an older
worker that sends none).

Not covered: nothing checks that a host registers its classifier in the worker process, which is
where remote runs are classified.

## Changelog

- **2026-09-27**: A cancellation nothing asked for, such as an `HttpClient` timeout, is a
  failure classified Transient rather than an unclassified Cancelled run.
- **2026-09-27**: A class in a message is carried, and recorded, only on a `TrainException`, and
  an undefined value as Unclassified.
- **2026-09-23**: Recorded.
