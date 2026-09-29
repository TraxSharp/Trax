---
authors: [Theauxm]
areas: [platform]
status: accepted
---

# The SignalR sink queues events, and drops them when clients fall behind

A train awaits its lifecycle hooks inline, so whatever a hook waits on, the train waits on. The
SignalR sink's hook therefore does not wait on delivery to clients. It filters the event and writes
it to a bounded queue; one background sender delivers the queue in order through `Clients.All`. When
the queue is full the event is dropped, counted and logged, and the train carries on.

## Status

**Accepted.**

## Why this is written down

Because awaiting the send is the obvious code, and it is what the sink did. `Clients.All` completes
only when every connection has taken the message, so the slowest client connected to the hub set the
pace of every train's `OnStarted`, `OnCompleted`, `OnFailed` and `OnStateChanged` in the process. A
client on a slow network, or one that stops reading, held each send for up to the transport's send
timeout, and `Run` did not return until it was over. Reintroducing an `await` on the hub in the hook
path brings that back.

## Considered options

**Fire the send and do not await it.** Keeps the hook fast, but every pending send holds its payload
until the slowest client takes it, with no limit on how many are pending. Rejected as unbounded.

**Only shorten the transport's send timeout.** `MapTraxTrainEventHub` does now set
`TransportSendTimeout` to 2 seconds rather than ASP.NET Core's 10, and a host can change it. That
bounds how long any one send can stall; it does not stop the train from waiting on it. It is kept as
a second measure, not as the fix.

**Block the hook when the queue is full.** Backpressure would lose nothing, but it is the original
problem with a larger buffer in front of it.

## Consequences

**Delivery to browsers is best effort.** An event can be dropped when clients fall behind by more
than the queue's capacity (`WithDeliveryQueueCapacity`, 1024 by default). A consumer that needs every
event reads it from the store or a durable transport, not from this sink. Events the sink does
deliver keep the order they were raised in.

**A stalled client still slows the sender for everyone.** `Clients.All` waits for every connection,
so one client that cannot take a send delays delivery to the others until the send timeout
disconnects it. The queue keeps that off the trains; the timeout keeps it short.

**Shutdown drains the queue.** The dispatcher is a hosted service. Stopping the host waits for queued
events to be sent, within the host's shutdown timeout, and abandons the rest when that runs out.

**The remote path is bounded too.** The RabbitMQ receiver that feeds remote events to the sink sets
a prefetch limit (`PrefetchCount`, 64 by default), so a slow handler leaves events on the broker
rather than accumulating them unacknowledged in the receiving process.

## Exemplars

- `SignalRSlowClientTests` connects a real WebSocket client that stops reading, and pins that no
  lifecycle send is held by it.
- [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub) is the rule this produces.

Not covered: the queue's drop, ordering and shutdown behaviour is pinned by ordinary unit tests
rather than a guard, and nothing stops a new hook elsewhere from awaiting a client send inline.

## Changelog

- **2026-09-28**: Recorded.
