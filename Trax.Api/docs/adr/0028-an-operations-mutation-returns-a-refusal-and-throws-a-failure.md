---
authors: [Theauxm]
areas: [graphql]
status: accepted
---

# An operations mutation returns a refusal and throws a failure

An operations mutation tells its caller about two kinds of outcome in two different places. A
**refusal**, something the caller can act on (an unknown train or id, input that does not read,
an empty or oversized batch, a target in the wrong state, a refusal the train itself makes), is
returned in the payload as `success: false` with a `message`, and writes nothing. A caller who
**may not** do it, and a **failure of the server** (a database or network failure, a run that
could not be submitted, a host misconfiguration), are GraphQL errors: `TRAX_AUTHORIZATION` with
`"Not authorized."` for the first, HotChocolate's masked `"Unexpected Execution Error"` for the
second, with the detail only in the server's logs. `runTrain`, `queueTrain`,
`requeueExecution` and the batch mutations all follow it, and they get it by calling
`IOperationsService`, which returns a refusal as a failed `OperationResult` and throws
everything else (scheduler/0004).

## Status

**Accepted.**

## Why this is written down

The split is the one Apollo's "errors as data" guidance and the GraphQL spec's error model draw:
the payload carries the outcomes a client is expected to handle and render, the top-level
`errors` array carries what the client cannot fix. Trax's `OperationResponse` predates the
reasoning, and a new mutation is easy to write the other way: catching everything into
`success: false` puts a connection string in `message`, and throwing a refusal makes a client
treat a bad request as an outage.

## Considered options

**Every outcome as data**, a union of typed error results per mutation. The fuller form of
errors as data, and it would make a refusal's kind machine-readable. Rejected for now: every
existing operations mutation returns `OperationResponse`, the dashboard reads `success` and
`message`, and the refusals carry no field a client branches on today. A typed union stays
possible for a new field that needs one.

**Every outcome as an error.** Simpler for a client that only checks `errors`, and it loses the
distinction an operator needs: a refused enqueue is not a server fault, and a masked error for
it hides the reason the caller could have acted on.

**An authorization failure as `success: false`.** Rejected: an unauthorized caller is refused
by the same `TRAX_AUTHORIZATION` error everywhere else in the API, and the payload of a field
the caller may not use should say nothing about the request, which is why authorization is
checked before the input is read.

## Exemplars

- `RunTrainMutationTests` pins each kind for `runTrain`: a refusal is a failed payload that
  writes no run, an unauthorized caller is `TRAX_AUTHORIZATION` even with malformed input, and a
  failed submit is the masked error.
- `OperationsFailureMaskingTests` pins the masked infrastructure failure for `queueTrain`.
- `OperationsBatchMutationsTests` pins that an empty or oversized batch is a failed payload.

Not covered: nothing checks a new mutation follows the split; it is reviewed. A refusal's
`message` is free text, not a code.

## Changelog

- **2026-09-30**: Recorded, with `runTrain`.
