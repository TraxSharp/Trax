---
authors: [Theauxm]
areas: [graphql]
status: accepted
---

# Only a TrainException's own message reaches the client

A `TrainException` message is passed to GraphQL clients because a train author writes it for
them. A `TrainException` can also carry another exception: a nested train's failure comes home
as `TrainExceptionData` JSON naming the exception type and message, and a run on a remote runner
fails as a `RemoteRunException` whose message is the calling side's full record of the failure,
the runner's reply included. Those carry text nobody wrote for a client. So `TraxErrorFilter`
passes a message through only when it is a train author's: a plain message, the carried message
of a carried `TrainException`, or a `RemoteRunException`'s `PublicMessage`, which the runner sets
only from a train author's own `TrainException`
([docs/0028](../../../Trax.Docs/adr/0028-a-remote-runs-client-message-is-chosen-by-the-runner.md)).
Every other carried type, and every remote failure without a public message, transport failures
included, becomes `"The train failed."` with the same `TRAX_TRAIN_ERROR` code. The full detail is
still recorded in the metadata row and the logs.

Only an exact `TrainException` is a train author's. A subclass carries whatever its author put in
it: Trax's own `TrainAlreadyStartedException` names a metadata id and the train's FullName, and a
consumer's subclass is as likely to wrap internal detail as to state a refusal. So a subclass reads
as `"The train failed."`, the rule the operations service already applies when `queueTrain` or
`runTrain` is refused, and a train is answered the same whichever surface ran it. The allowlist is
`RemoteRunException`, whose public message the runner chose.

A mapped error is also detached from its exception. HotChocolate writes an attached exception's
message and stack trace into the response when a host switches exception details on, after the
filters have run, so a mapped error that kept it would carry everything its public message leaves
out. An unmapped exception keeps it, as HotChocolate's own development detail.

## Status

**Accepted.**

## Considered options

**Pass everything through, as before.** Simple, and the carried JSON is useful when debugging.
It also means the client sees whatever the worker's exception said, which for a database or
network failure is internal detail.

**Mask every TrainException.** Safe, and it throws away the one message that is meant for the
client: a train author's refusal ("Order 42 is already closed") would read the same as a
crashed worker.

**Pass a subclass's message, or mark a public one.** The filter passed any subclass's message
until 2026-10-01, which made the typed train field and the operations refusal disagree about the
same exception. A public-message marker on `TrainException` would let a subclass opt in, but it
belongs in Trax.Core and every consumer would have to learn it; a plain `TrainException` already
says "this message is for the client".

**Recognise the scheduler's remote-transport messages by prefix.** What this filter did until
Trax.Scheduler 1.34.0 shipped `RemoteRunException.PublicMessage`. It coupled the API to strings
another repo writes, and it missed the Lambda executor's `"Lambda function '…' returned error"`,
which matched neither prefix. Replaced by the public message, the end state this option was
waiting for.

## Exemplars

- `TraxErrorFilterTests` pins the public shape of each exception type, including a remote
  failure carrying a driver exception, a remote `TrainException` with and without a public
  message, a remote failure with no type, a non-success status from the remote endpoint, a
  Lambda function error, a subclass, `TrainAlreadyStartedException`, and a plain `TrainException` whose wording resembles a transport
  failure (it passes through: the filter reads the type, not the text), and that every mapped
  case leaves no exception attached.
- `TrainExceptionMessageRuleTests` pins both rules end to end: a `TrainException` and a subclass
  are answered the same on a typed train field and on `queueTrain`, and no mapped error carries
  an `exception` extension with HotChocolate's exception details on.

## Changelog

- **2026-10-01**: Narrowed to an exact `TrainException`, matching the operations refusal path, and
  every mapped error is detached from its exception.
- **2026-09-30**: A remote failure is read from `RemoteRunException.PublicMessage` (docs/0028)
  instead of by the transport messages' prefixes, which missed the Lambda executor's.
- **2026-09-27**: Recorded.
