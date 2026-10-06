---
layout: default
title: Error Codes
description: "Every TRAX_* and PERSISTED_OPERATION_* code the Trax GraphQL endpoint returns in extensions.code, and what raises it."
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 12
---

# Error Codes

Every error the Trax GraphQL endpoint raises itself carries a stable code in `extensions.code`, so a client branches on the code and never on the message. Messages are written for people and may change; codes do not.

```json
{ "errors": [{ "message": "Not authorized.", "extensions": { "code": "TRAX_AUTHORIZATION" } }] }
```

HotChocolate's own codes (`HC0020` for an unknown persisted operation id, the `AUTH_*` codes an authorization handler reports when the host is misconfigured, and the rest) pass through unchanged. The two authorization-failure codes, `AUTH_NOT_AUTHENTICATED` and `AUTH_NOT_AUTHORIZED`, are the exception: Trax rewrites both to `TRAX_AUTHORIZATION`, so a caller sees one shape whichever check refused it.

## Request errors

These arrive in the response's top-level `errors` array.

| Code | Message | Raised by |
|---|---|---|
| `TRAX_AUTHORIZATION` | `Not authorized.` | Any authorization refusal. The `@authorize` directive on a `[TraxAuthorize]` query model, field or navigation target, and the filter and sort inputs that reach one; a train's `[TraxAuthorize]` requirements on `dispatch`, `queueTrain`, `runTrain` and `requeueExecution` (`TrainAuthorizationException`); the operations gate (`GateOperations`); the endpoint policy (`RequireAuthorization`), over HTTP and for every operation on a socket; a subscription that could receive nothing, refused when it subscribes. The train, policy and role names never appear in it. See [Authorization](/docs/authorization). |
| `TRAX_TRAIN_NOT_FOUND` | `The requested train was not found.` | `TrainNotFoundException`: a train name that matches no registered train. The name sent is not echoed back. |
| `TRAX_AMBIGUOUS_TRAIN` | Lists the candidate FullNames | `AmbiguousTrainNameException`: a short train name that matches more than one registered train. |
| `TRAX_INVALID_INPUT` | `The train input failed validation.` | `TrainInputValidationException`: input JSON that does not deserialize to the train's input type, or that is larger than the mediator's `WithMaxInputJsonBytes` limit (256 KiB by default). |
| `TRAX_TRAIN_ERROR` | The train's own message, or `The train failed.` | A plain `TrainException` the train threw: its message passes through, since a train author wrote it. A subclass of `TrainException`, a host's own or Trax's, shows `The train failed.`, the same as `queueTrain` and `runTrain`. A remote run's failure (`RemoteRunException`) shows the runner's public message, and any other carried failure shows `The train failed.`; the detail stays in the metadata row and the server log. |
| `TRAX_HOST_CONFIGURATION` | `The train could not be run.` | `NoTrainForInputException`: no registered train takes the input. The full exception, naming the input type and the assemblies the host scanned, is logged at Error and never sent, even with HotChocolate's exception details on. |
| `TRAX_TOO_MANY_OPERATIONS` | `The request exceeds the maximum allowed operations per request (n).` | Validation, before any resolver runs: the request invokes more operations than `MaxOperationsPerRequest` allows (50 by default). See [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql). |
| `TRAX_SOCKET_OPERATION_LIMIT` | `This connection already runs as many operations as it may. Complete one before starting another.` | An operation started on a WebSocket connection that already runs `MaxOperationsPerConnection` operations (100 by default). The connection stays open. |
| `TRAX_SKIP_TOO_DEEP` | `skip may be at most 10000. To read further, page with afterId: …` | A paged operations read (`executions`, `manifests`, `workQueues`, `deadLetters`, `logs`, `groups`) with `skip` above 10,000. The error carries `extensions.maxSkip`. Page deeper with `afterId`, passing each page's `nextCursor`. See [Queries](/docs/sdk-reference/graphql-api/queries). |
| `TRAX_TOO_MANY_IDS` | `At most 1000 group ids can be given at once; n were.` | [`operations.manifestGroups.stats`](/docs/sdk-reference/graphql-api/queries#stats) with more than 1000 distinct group ids. |
| `PERSISTED_OPERATION_REQUIRED` | `Only persisted operations are accepted on this server.` | Persisted-operation enforcement: an inline document the server does not accept. HTTP status 400. See [UsePersistedOperationsEnforcement](/docs/sdk-reference/persisted-operations/use-persisted-operations-enforcement). |
| `PERSISTED_OPERATION_ID_MISMATCH` | `A request that names a persisted operation id may carry a document only when the id is that document's hash. …` | A request that sends both a persisted operation id and a document, where the id is not the document's own hash under the executor's hash algorithm. HTTP status 400. See [Persisted Operations](/docs/persisted-operations). |

An operations mutation that the service refuses (an empty or oversized batch, a run that is not cancellable, a manifest external id that names no manifest, an acknowledgement note over 1,000 characters, a manifest update the scheduler could not run) is not an error: it returns `success: false` with a message in its payload. See [Mutations](/docs/sdk-reference/graphql-api/mutations).

## Persisted-operation management codes

The [persisted-operation management mutations](/docs/sdk-reference/persisted-operations/management-mutations) (`uploadPersistedOperation`, `deactivatePersistedOperation`, `restorePersistedOperation`) never put a failure in the top-level `errors`. They return it in the payload's own `errors` field, each entry with a `code`:

| Code | When |
|---|---|
| `PARSE_FAILED` | The uploaded document does not parse. |
| `SCHEMA_VALIDATION_FAILED` | The uploaded document parses but fails validation against the schema. One entry per validation failure. |
| `SHAPE_DIFF_VIOLATION` | The upload changes the response shape of an existing id. |
| `INVALID_INPUT` | A required field is empty, or the document holds no operation or more than one. |
| `NOT_FOUND` | Deactivate or restore names an id the store does not hold. |
| `CHANGE_NOT_BROADCAST` | The change was saved and is in force on the node that made it, but the broker did not confirm the message telling the other nodes. The payload carries the saved operation beside this error and `success` is false; the other nodes pick the change up within the cache's maximum age (`WithCacheMaxAge`, five minutes by default), and repeating the change sends it again. |

`PARSE_FAILED`, `SCHEMA_VALIDATION_FAILED`, `SHAPE_DIFF_VIOLATION`, `INVALID_INPUT` and `CHANGE_NOT_BROADCAST` are the `Code` of the matching [PersistedOperationException](/docs/sdk-reference/persisted-operations/persisted-operation-exceptions) subclass, which a host calling `IPersistedOperationStore.UpsertAsync` directly catches instead.
