---
layout: default
title: TraxAuditOptions
description: Reference for TraxAuditOptions, the tunables of the GraphQL audit pipeline such as ChannelCapacity, BatchSize and FlushInterval, with tuning guidance.
parent: API Audit
grand_parent: SDK Reference
---

# TraxAuditOptions

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Tunables for the audit pipeline, passed through `AddAudit<TSink>(opts => ...)`.

| Property | Default | Purpose |
|---|---|---|
| `ChannelCapacity` | `10_000` | Max queued entries. Overflow drops the new entry and increments the `trax.audit.dropped` meter. |
| `BatchSize` | `50` | Max entries handed to the sink in one call. |
| `FlushInterval` | `500ms` | Max time to wait before flushing a partial batch. |
| `MaxDocumentLength` | `65_536` | Documents longer than this are cut to this length and marked `...[truncated]`, then followed by `[selected fields: ...]`, every `Type.field` the operation selects. The list names each schema coordinate once, so its size is bounded by the schema rather than by the request. |
| `SkipIntrospection` | `true` | Drop introspection operations: the executed operation's top-level selections are all `__schema`, `__type` or `__typename`. |
| `SkipSubscriptions` | `true` | Drop subscription operations. They don't fit a request/response audit model. |
| `DefaultPrincipalId` | `"<anonymous>"` | Used when the request has no `trax:principal-id` claim. |
| `MaxRetries` | `3` | Retries a failing sink gets after its first attempt before the batch is dropped, so the default makes 4 attempts in all. Each dropped entry increments `trax.audit.dropped`. |
| `RetryBackoff` | `100ms` | Initial backoff between sink retries. Doubles on each attempt. |

## Tuning Guidance

- **High-traffic hosts**: raise `ChannelCapacity`, keep `BatchSize` modest (50-100), aim for a `FlushInterval` that matches your sink's latency.
- **Expensive sinks** (Postgres, S3): larger batches amortize I/O. Raise `BatchSize` to 200+ and extend `FlushInterval` accordingly.
- **Regulated workloads**: set `MaxRetries` high enough that transient sink outages don't cause drops. Monitor `trax.audit.dropped` and page on non-zero. It counts every lost entry: refused by a full channel, refused by the sink after every retry, or unwritten when shutdown ran out of time.
- **Shutdown**: the writer drains every accepted entry on graceful shutdown, within `HostOptions.ShutdownTimeout` (30 seconds by default). Raise that timeout if your sink needs longer to write a full channel.
