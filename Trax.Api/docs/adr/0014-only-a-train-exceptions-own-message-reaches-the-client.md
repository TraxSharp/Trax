---
authors: [Theauxm]
areas: [graphql]
status: accepted
---

# Only a TrainException's own message reaches the client

A `TrainException` message is passed to GraphQL clients because a train author writes it for
them. A `TrainException` can also carry another exception: a remote worker's failure comes home
as `TrainExceptionData` JSON naming the worker's exception type and message, and the scheduler's
remote executors wrap a worker's HTTP reply in a message of their own. Those carry text nobody
wrote for a client. So `TraxErrorFilter` passes a message through only when it is a train
author's: a plain message, or the carried message of a carried `TrainException`. Every other
carried type, and the remote-transport messages, become `"The train failed."` with the same
`TRAX_TRAIN_ERROR` code. The full detail is still recorded in the metadata row and the logs.

## Status

**Accepted.**

## Considered options

**Pass everything through, as before.** Simple, and the carried JSON is useful when debugging.
It also means the client sees whatever the worker's exception said, which for a database or
network failure is internal detail.

**Mask every TrainException.** Safe, and it throws away the one message that is meant for the
client: a train author's refusal ("Order 42 is already closed") would read the same as a
crashed worker.

**Have the scheduler carry a separate public message.** The better end state, because it stops
the filter from reading the scheduler's message formats. It needs a scheduler release and a
new field on the remote response; the filter's rule is what holds until then, and it stays
correct afterwards.

## Exemplars

- `TraxErrorFilterTests` pins the public shape of each exception type, including a remote
  failure carrying a driver exception, a carried `TrainException`, a remote failure with no
  type, and a non-success status from the remote endpoint.

Not covered: the remote-transport messages are recognised by the prefixes the scheduler builds
today. A new message format in the scheduler that does not use them passes through until the
filter learns it.

## Changelog

- **2026-09-27**: Recorded.
