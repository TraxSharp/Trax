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

## Status

**Accepted.**

## Considered options

**Pass everything through, as before.** Simple, and the carried JSON is useful when debugging.
It also means the client sees whatever the worker's exception said, which for a database or
network failure is internal detail.

**Mask every TrainException.** Safe, and it throws away the one message that is meant for the
client: a train author's refusal ("Order 42 is already closed") would read the same as a
crashed worker.

**Recognise the scheduler's remote-transport messages by prefix.** What this filter did until
Trax.Scheduler 1.34.0 shipped `RemoteRunException.PublicMessage`. It coupled the API to strings
another repo writes, and it missed the Lambda executor's `"Lambda function '…' returned error"`,
which matched neither prefix. Replaced by the public message, the end state this option was
waiting for.

## Exemplars

- `TraxErrorFilterTests` pins the public shape of each exception type, including a remote
  failure carrying a driver exception, a remote `TrainException` with and without a public
  message, a remote failure with no type, a non-success status from the remote endpoint, a
  Lambda function error, and a plain `TrainException` whose wording resembles a transport
  failure (it passes through: the filter reads the type, not the text).

## Changelog

- **2026-09-30**: A remote failure is read from `RemoteRunException.PublicMessage` (docs/0028)
  instead of by the transport messages' prefixes, which missed the Lambda executor's.
- **2026-09-27**: Recorded.
