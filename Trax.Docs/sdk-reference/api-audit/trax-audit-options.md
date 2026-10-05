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
| `MaxOperationNameLength` | `256` | Operation names longer than this are cut to this length and marked `...[truncated]`. The name is whatever the caller sent. |
| `MaxErrorTextLength` | `4_096` | `ErrorText` longer than this is cut to this length and marked `...[truncated]`. A request can raise one error per field it selects. |
| `RecordErrorMessages` | `false` | Record error messages in `ErrorText`. Off, each error is recorded as `CODE at path` (`<masked>` when it has no code) and a pipeline exception as its type name, because a message can quote what the caller sent. |
| `SkipIntrospection` | `true` | Drop introspection operations that succeeded: the executed operation's top-level selections are all `__schema`, `__type` or `__typename`. One that raised an error is recorded. |
| `SkipSubscriptions` | `true` | Drop subscriptions that were accepted (returned a stream). A subscription refused or failed when it subscribes is always recorded. |
| `DefaultPrincipalId` | `"<anonymous>"` | Used when the request has no `trax:principal-id` claim. |
| `MaxRetries` | `3` | Retries a failing sink gets after its first attempt before the batch is dropped, from 0 to 100, so the default makes 4 attempts in all. Each dropped entry increments `trax.audit.dropped`. |
| `RetryBackoff` | `100ms` | Initial backoff between sink retries. Doubles on each attempt, up to `MaxRetryBackoff`; each wait is a random point between half and all of that value. |
| `MaxRetryBackoff` | `30s` | The longest wait between two sink retries, whatever the attempt. |

## Validation

`AddAudit` validates the options when the host starts and refuses to start, with an `OptionsValidationException` naming each option out of range, when:

- `ChannelCapacity`, `BatchSize`, `MaxDocumentLength`, `MaxOperationNameLength` or `MaxErrorTextLength` is 0 or less;
- `FlushInterval` is zero or less;
- `MaxRetries` is below 0 or above 100;
- `MaxRetryBackoff` is zero or less, or above one hour;
- `RetryBackoff` is negative or above `MaxRetryBackoff`;
- `DefaultPrincipalId` is empty.

## Tuning Guidance

- **High-traffic hosts**: raise `ChannelCapacity`, keep `BatchSize` modest (50-100), aim for a `FlushInterval` that matches your sink's latency.
- **Expensive sinks** (Postgres, S3): larger batches amortize I/O. Raise `BatchSize` to 200+ and extend `FlushInterval` accordingly.
- **Regulated workloads**: set `MaxRetries` high enough that transient sink outages don't cause drops; with the 30 second cap, 15 retries hold one batch for at most about four minutes, during which the channel fills behind it. Monitor `trax.audit.dropped` and page on non-zero. It counts every lost entry: one the listener failed to capture or build, refused by a full channel, refused by the sink after every retry, or unwritten when shutdown ran out of time.
- **Shutdown**: the writer drains every accepted entry on graceful shutdown, within `HostOptions.ShutdownTimeout` (30 seconds by default). Raise that timeout if your sink needs longer to write a full channel.
