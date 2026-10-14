---
title: Remote error responses
description: "The error fields a Trax runner returns from /trax/execute and /trax/run (RemoteJobResponse, RemoteRunResponse), and how the calling side records and shows them."
type: reference
area: scheduling
nav_order: 23
source:
  - Trax.Scheduler/src/Trax.Scheduler/Services/RunExecutor/RemoteRunResponse.cs
  - Trax.Scheduler/src/Trax.Scheduler/Services/JobSubmitter/RemoteJobResponse.cs
  - Trax.Scheduler/src/Trax.Scheduler/Services/RunExecutor/RemoteRunException.cs
---

# Remote error responses

A runner answers both endpoints with a structured body. `/trax/execute` (queued jobs) returns a
`RemoteJobResponse`; `/trax/run` (synchronous runs) and a Lambda `Run` return a `RemoteRunResponse`.

## Error fields

| Field | `RemoteJobResponse` | `RemoteRunResponse` | Value |
|-------|:---:|:---:|-------|
| `IsError` | yes | yes | Whether the execution failed |
| `ErrorMessage` | yes | yes | The message of a `TrainException`. Any other exception is reported with a fixed message; the detail stays in the runner's log |
| `ExceptionType` | yes | yes | The .NET exception type name, such as `"InvalidOperationException"` |
| `StackTrace` | yes | yes | Always null. No stack trace leaves the runner; the run's metadata row and the runner's log hold it |
| `FailureJunction` | | yes | The junction where the failure occurred |
| `FailureClass` | | yes | The [failure class](/docs/effect/failure-classification) the runner's classifier assigned, or null when it sent none. A class outside the `FailureClass` values is sent as `Unclassified` |
| `PublicMessage` | | yes | The message a client of the calling side may see: the message of a plain `TrainException`, null for every other failure |

`RemoteRunResponse` also carries `MetadataId`, `ExternalId`, and on success `OutputJson` and
`OutputType`.

On the runner, the fields come from the `TrainExceptionData` attached to the exception, or, for a
`TrainException` rebuilt from an earlier boundary, from the JSON in its message.

## On the calling side

| Path | Behavior |
|------|----------|
| `/trax/execute` | An error response fails the dispatch with a plain `TrainException` whose message is `Remote worker reported error: {ErrorMessage} [{ExceptionType}]`. The runner has already written the failure to the run's metadata row. A `200` whose body is not a `RemoteJobResponse` for the job sent also fails the dispatch |
| `/trax/run`, Lambda `Run` | The run executor rebuilds a `RemoteRunException` (a `TrainException`) carrying `PublicMessage`, with the type, junction, message and class as `TrainExceptionData` JSON. `Metadata.AddException()` fills `FailureException`, `FailureJunction`, `FailureReason` and `FailureClass` from it. A null `FailureClass` records as `Unclassified`; the calling side's own classifier is not asked |
| Transport failure | A non-success status, a Lambda function error or an empty reply is a `RemoteRunException` with no public message. The exception message includes the status code and the response body, for the row and the logs |

The runner endpoints and the Lambda runner's local HTTP route write `RemoteRunResponse` with Trax's
own JSON options (enums as integers) whatever the host's JSON configuration. A Lambda function's
invocation response is serialized by the function's Lambda serializer instead, so the HTTP and
Lambda run executors read `FailureClass` as an integer or a name. A class they do not know, such as
one from a newer runner, reads as `Unclassified` and the runner's error is kept.

## What a GraphQL client sees

When a remote run's failure reaches the GraphQL error filter, only a plain `TrainException`'s own
message passes through; a subclass reads as a failed train. A runner that threw
`TrainException("Order 42 is already closed")` shows the client that sentence. A runner that failed
with any other exception, or an endpoint that answered with a non-success status, shows
`"The train failed."` with code `TRAX_TRAIN_ERROR`. `Trax.Docs/adr/0028` records why.

## See also

- [Debug a failed remote run](/docs/scheduler/debug-remote-failures)
- [Choosing where trains run](/docs/scheduler/remote-execution#errors-across-the-boundary)
- [JobDispatcher: Dispatch failures](/docs/scheduler/admin-trains/job-dispatcher#dispatch-failures)
