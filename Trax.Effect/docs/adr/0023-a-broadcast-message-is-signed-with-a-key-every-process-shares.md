---
authors: [Theauxm]
areas: [auth, platform]
status: accepted
---

# A broadcast message is signed with a key every process shares

The RabbitMQ broadcaster signs every message it publishes with a key every process on the exchange
holds (`RabbitMqBroadcasterOptions.SigningKey`, at least 32 bytes): HMAC-SHA256 over the body, the
exchange, a timestamp and a message id, in a `trax-signature` header. A receiver checks it before it
reads the body and drops a message that is unsigned, signed with another key, or further than
`MaxClockSkew` from its clock. `UseRabbitMq` refuses to start without a key unless the host calls
`AllowUnsignedMessages()`, and refuses plain `amqp://` to a host that is not loopback unless it sets
`AllowInsecureTransport`.

## Status

**Accepted.**

## Why this is written down

A receiver hands every message to the host's handlers as a train's lifecycle event, and those
reach GraphQL subscribers, the SignalR hub and the host's own hooks. The broker's credentials are
the only thing between the exchange and those handlers, and they are usually one account shared by
every service on the broker. So anything that can publish to the exchange could put a completed run,
with any output, in front of every subscriber. A broker is reached without a key or without TLS
only because a host said so, by name.

## Considered options

**Rely on the broker's permissions.** RabbitMQ can restrict who writes to an exchange, but Trax
cannot see or check that configuration, and the default user on a fresh broker can write anywhere.
The key is a check Trax makes itself.

**Sign with a key per publisher, or asymmetric keys.** Would tell publishers apart, and let a
receiver hold only public keys. Every Trax process on an exchange both publishes and receives, so
each would hold a private key anyway, and the host would have to distribute and rotate one per
process. A shared HMAC key is one secret in configuration, the shape the runner's signing key
already has (`Trax.Scheduler`).

**Accept unsigned messages by default and warn.** The rest of Trax refuses an unsafe posture at
startup rather than logging it, and a warning on a long-running host is read
by nobody.

**Encrypt the body as well.** TLS to the broker already keeps it off the wire; what reaches the
broker is readable by whoever administers it, which a key in every process does not change.

## Consequences

- Upgrading a host that used `UseRabbitMq(connectionString)` alone stops it at startup until it sets
  a key or opts out. Every process on the exchange needs the same key, so they are upgraded together.
- A timestamp further than `MaxClockSkew` (five minutes) from the receiver's clock is dropped, so a
  host's clocks have to agree. A message replayed within the skew is accepted again: the receivers
  keep no memory of message ids, and a train event delivered twice is already tolerated.
- Dropped messages are logged as a warning at most once a minute, with the count since, so a flood
  of forged messages does not flood the log.
- Trax.Api's persisted-operation invalidation over RabbitMQ (`UseRabbitMqInvalidation`) signs with
  the same scheme, its own key and the same opt-out names, since a forged invalidation empties
  every node's caches.

## Exemplars

- `RabbitMqMessageSigningTests` pins the signature (a changed body, exchange, key or timestamp does
  not match; a stale one is dropped), the refusals at startup without a key or with plain `amqp://`
  to another host, the two opt-outs, and that a receiver passes on only the messages signed with its
  key.
- [UseRabbitMq: Signing and transport](/docs/sdk-reference/configuration/use-rabbit-mq#signing-and-transport)
  is the rule this produces.

Not covered: nothing checks that every process on an exchange holds the same key; a process with
another key starts, and its events are dropped by the others.

## Changelog

- **2026-10-23**: Recorded.
