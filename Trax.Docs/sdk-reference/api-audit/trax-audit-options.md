---
layout: default
title: TraxAuditOptions
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
| `MaxDocumentLength` | `65_536` | Documents longer than this are truncated with a `...[truncated]` marker. |
| `SkipIntrospection` | `true` | Drop requests whose operation name is `IntrospectionQuery`. |
| `SkipSubscriptions` | `true` | Drop subscription operations. They don't fit a request/response audit model. |
| `DefaultPrincipalId` | `"<anonymous>"` | Used when the request has no `trax:principal-id` claim. |
| `MaxRetries` | `3` | Attempts a failing sink gets before the batch is dropped. |
| `RetryBackoff` | `100ms` | Initial backoff between sink retries. Doubles on each attempt. |

## Tuning Guidance

- **High-traffic hosts**: raise `ChannelCapacity`, keep `BatchSize` modest (50-100), aim for a `FlushInterval` that matches your sink's latency.
- **Expensive sinks** (Postgres, S3): larger batches amortize I/O. Raise `BatchSize` to 200+ and extend `FlushInterval` accordingly.
- **Regulated workloads**: set `MaxRetries` high enough that transient sink outages don't cause drops. Monitor `trax.audit.dropped` and page on non-zero.
