# Decisions

Why a thing in `Trax.Api` is the way it is, which alternatives were weighed, and what each
cost. A documentation page tells you what the rule *is*; an ADR tells you whether it is a
deliberate constraint or an accident, so you can tell which ones are safe to change.

Read the relevant one before proposing to change a rule. If your work contradicts one, say
so rather than silently overriding it.

## Scope

**These bind `Trax.Api` only.** A decision binding more than one Trax repo lives in the
central corpus, at `Trax.Docs/adr/`, and declares which repos must obey it. These omit that
key, because the path already says it.

Numbering is per directory, so `0001` exists in several repos. Cite one of these as
`api/0001`.

## How they are checked

The `adr-guard` job in `.github/workflows/pull_request.yml` runs the guard published by
Trax.Docs against this directory on every pull request. The job needs nothing else cloned.
The local command does, because it builds the guard out of a workspace checkout:

```bash
dotnet run --project ../Trax.Docs/tools/Trax.Adr.Guard -- \
  --repo . \
  --known-areas graphql,auth,platform,testing \
  --census-root tests/Trax.Api.Tests.Meta
```

Without `--census-root` the census is never added to the run, so nothing named
`census/classified` is printed and the local command is weaker than the job above.

The format is `.claude/skills/recording-decisions/ADR-FORMAT.md`.

## By area

| Area | ADRs |
| --- | --- |
| `auth` | [0003](./0003-a-type-extension-field-declares-its-own-posture.md), [0004](./0004-the-operations-namespace-gates-independently-of-the-endpoint.md), [0005](./0005-the-allowlist-keys-on-the-caller-supplied-operation-name.md), [0006](./0006-one-socket-interceptor-composes-every-token-scheme.md), [0007](./0007-a-browser-socket-is-accepted-only-from-origins-the-host-serves.md), [0008](./0008-a-token-scheme-requires-a-credential-on-every-socket.md), [0009](./0009-the-endpoint-policy-applies-to-every-transport.md), [0010](./0010-a-scheme-policy-requires-its-scheme.md), [0011](./0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md), [0012](./0012-introspection-is-decided-per-request.md), [0016](./0016-a-train-input-is-read-one-row-at-a-time.md), [0022](./0022-a-socket-authenticates-through-the-schemes-handler.md), [0023](./0023-a-principal-id-is-qualified-by-its-scheme.md), [0024](./0024-graphql-get-is-off-unless-the-host-opts-in.md), [0025](./0025-every-entity-a-query-model-reaches-declares-its-posture.md), [0027](./0027-an-audit-entry-records-no-value-the-caller-sent-unless-the-host-opts-in.md), [0030](./0030-separate-authorization-attributes-combine-with-and.md), [0033](./0033-a-socket-connection-has-a-maximum-lifetime-and-re-checks-its-key.md), [0035](./0035-a-refused-request-is-always-audited.md), [0037](./0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md) |
| `graphql` | [0001](./0001-a-misconfigured-host-fails-at-startup.md), [0002](./0002-reading-the-service-collection-is-order-dependent.md), [0003](./0003-a-type-extension-field-declares-its-own-posture.md), [0004](./0004-the-operations-namespace-gates-independently-of-the-endpoint.md), [0005](./0005-the-allowlist-keys-on-the-caller-supplied-operation-name.md), [0006](./0006-one-socket-interceptor-composes-every-token-scheme.md), [0007](./0007-a-browser-socket-is-accepted-only-from-origins-the-host-serves.md), [0008](./0008-a-token-scheme-requires-a-credential-on-every-socket.md), [0009](./0009-the-endpoint-policy-applies-to-every-transport.md), [0010](./0010-a-scheme-policy-requires-its-scheme.md), [0011](./0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md), [0012](./0012-introspection-is-decided-per-request.md), [0013](./0013-persisted-operation-enforcement-runs-in-the-execution-pipeline.md), [0014](./0014-only-a-train-exceptions-own-message-reaches-the-client.md), [0015](./0015-a-socket-runs-a-bounded-number-of-operations.md), [0016](./0016-a-train-input-is-read-one-row-at-a-time.md), [0017](./0017-an-operations-page-is-at-most-500-rows.md), [0022](./0022-a-socket-authenticates-through-the-schemes-handler.md), [0024](./0024-graphql-get-is-off-unless-the-host-opts-in.md), [0025](./0025-every-entity-a-query-model-reaches-declares-its-posture.md), [0026](./0026-a-persisted-operation-id-means-one-document-on-every-node.md), [0027](./0027-an-audit-entry-records-no-value-the-caller-sent-unless-the-host-opts-in.md), [0028](./0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md), [0029](./0029-the-operation-cap-counts-the-operations-under-each-namespace.md), [0030](./0030-separate-authorization-attributes-combine-with-and.md), [0031](./0031-a-request-names-the-client-that-validates-it.md), [0032](./0032-the-lifecycle-feed-is-lossy-and-numbers-its-events.md), [0033](./0033-a-socket-connection-has-a-maximum-lifetime-and-re-checks-its-key.md), [0034](./0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md), [0035](./0035-a-refused-request-is-always-audited.md), [0036](./0036-requeue-all-runs-in-the-background-and-returns-a-handle.md), [0037](./0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md), [0038](./0038-a-queued-mutation-is-refused-where-nothing-dispatches-it.md) |
| `platform` | [0001](./0001-a-misconfigured-host-fails-at-startup.md), [0002](./0002-reading-the-service-collection-is-order-dependent.md), [0031](./0031-a-request-names-the-client-that-validates-it.md) |

## All of them

| # | Decision | Areas |
| --- | --- | --- |
| [0001](./0001-a-misconfigured-host-fails-at-startup.md) | A misconfigured host fails at startup, not at request time | graphql, platform |
| [0002](./0002-reading-the-service-collection-is-order-dependent.md) | Reading the service collection during registration is order-dependent | graphql, platform |
| [0003](./0003-a-type-extension-field-declares-its-own-posture.md) | A type-extension field on an anonymous parent declares its own posture | graphql, auth |
| [0004](./0004-the-operations-namespace-gates-independently-of-the-endpoint.md) | The operations namespace gates independently of the endpoint | graphql, auth |
| [0005](./0005-the-allowlist-keys-on-the-caller-supplied-operation-name.md) | The persisted-operations allowlist keys on the caller-supplied operation name | graphql, auth |
| [0006](./0006-one-socket-interceptor-composes-every-token-scheme.md) | One socket interceptor composes every token scheme | graphql, auth |
| [0007](./0007-a-browser-socket-is-accepted-only-from-origins-the-host-serves.md) | A browser socket is accepted only from origins the host serves | graphql, auth |
| [0008](./0008-a-token-scheme-requires-a-credential-on-every-socket.md) | A token scheme requires a credential on every socket | graphql, auth |
| [0009](./0009-the-endpoint-policy-applies-to-every-transport.md) | The endpoint policy applies to every transport | graphql, auth |
| [0010](./0010-a-scheme-policy-requires-its-scheme.md) | A scheme policy requires its scheme | graphql, auth |
| [0011](./0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md) | Subscriptions carry the authorization of the data they stream | graphql, auth |
| [0012](./0012-introspection-is-decided-per-request.md) | Introspection is decided per request | graphql, auth |
| [0013](./0013-persisted-operation-enforcement-runs-in-the-execution-pipeline.md) | Persisted-operation enforcement runs in the execution pipeline | graphql |
| [0014](./0014-only-a-train-exceptions-own-message-reaches-the-client.md) | Only a TrainException's own message reaches the client | graphql |
| [0015](./0015-a-socket-runs-a-bounded-number-of-operations.md) | A socket runs a bounded number of operations | graphql |
| [0016](./0016-a-train-input-is-read-one-row-at-a-time.md) | A train input is read one row at a time | graphql, auth |
| [0017](./0017-an-operations-page-is-at-most-500-rows.md) | An operations page is at most 500 rows, and take 0 returns one | graphql |
| [0022](./0022-a-socket-authenticates-through-the-schemes-handler.md) | A socket authenticates through the scheme's handler | graphql, auth |
| [0023](./0023-a-principal-id-is-qualified-by-its-scheme.md) | A principal id is qualified by its scheme | auth |
| [0024](./0024-graphql-get-is-off-unless-the-host-opts-in.md) | GraphQL GET is off unless the host opts in | graphql, auth |
| [0025](./0025-every-entity-a-query-model-reaches-declares-its-posture.md) | Every entity a query model reaches declares its posture | graphql, auth |
| [0026](./0026-a-persisted-operation-id-means-one-document-on-every-node.md) | A persisted-operation id means one document, on every node | graphql |
| [0027](./0027-an-audit-entry-records-no-value-the-caller-sent-unless-the-host-opts-in.md) | An audit entry records no value the caller sent unless the host opts in | graphql, auth |
| [0028](./0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md) | An operations mutation returns a refusal and throws a failure | graphql |
| [0029](./0029-the-operation-cap-counts-the-operations-under-each-namespace.md) | The operation cap counts the operations under each namespace | graphql |
| [0030](./0030-separate-authorization-attributes-combine-with-and.md) | Separate authorization attributes combine with AND | graphql, auth |
| [0031](./0031-a-request-names-the-client-that-validates-it.md) | A request names the client that validates it | graphql, platform |
| [0032](./0032-the-lifecycle-feed-is-lossy-and-numbers-its-events.md) | The lifecycle feed is lossy and numbers its events | graphql |
| [0033](./0033-a-socket-connection-has-a-maximum-lifetime-and-re-checks-its-key.md) | A socket connection has a maximum lifetime, and an API-key socket re-checks its key | graphql, auth |
| [0034](./0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md) | A cached persisted operation is never older than the last change or its maximum age | graphql |
| [0035](./0035-a-refused-request-is-always-audited.md) | A refused request is always audited | graphql, auth |
| [0036](./0036-requeue-all-runs-in-the-background-and-returns-a-handle.md) | Requeue-all runs in the background and returns a handle | graphql |
| [0037](./0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md) | A run's step feed follows one run, with its train's visibility | graphql, auth |
| [0038](./0038-a-queued-mutation-is-refused-where-nothing-dispatches-it.md) | A queued mutation is refused where nothing dispatches it | graphql |
