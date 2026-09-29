---
authors: [Theauxm]
areas: [samples]
status: accepted
---

# Sample infrastructure listens on loopback only

Every port `docker-compose.yml` publishes is bound to `127.0.0.1`, and its containers restart
`unless-stopped`. The file writes its credentials in plain text (`trax`/`trax123` for Postgres
and for RabbitMQ), so a port published on every interface is a database and a broker admin
console that anyone on the same network can log in to. Loopback keeps them reachable from
the samples, the tests and a database client on the same machine, which is everyone the file
is for.

## Status

**Accepted.**

## Considered options

**Publish on every interface, as Docker does by default.** It lets another machine or a phone
on the LAN reach the database, which nobody here needs, and it is the default a reader copies
into a server's compose file along with the password.

**Generate a random password per checkout.** It hides nothing the loopback binding does not
already, and it breaks the connection strings the samples, the E2E factories and CI all share.

## Consequences

A container on another Docker network, or a process on another host, cannot reach these
services. Something that needs to should join the compose network rather than widen the
binding. `TRAX_PG_PORT` still picks the host port; it only moves it, never the interface.

`restart: unless-stopped` replaces `restart: always`, so a database stopped by hand stays
stopped across a Docker restart instead of coming back on its own.

## Exemplars

- `ComposePortsBindLoopbackTests` reads every compose file in the repository and fails on a
  published port without the `127.0.0.1:` prefix.

Not covered: the restart policy, and the passwords themselves. The guard reads the
short-syntax port list only; a long-syntax entry fails it rather than being understood.

## Changelog

- **2026-09-27**: Recorded.
