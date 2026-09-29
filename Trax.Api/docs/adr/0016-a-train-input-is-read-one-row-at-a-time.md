---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# A train input is read one row at a time

A train's input is whatever the train takes, and that includes credentials: an API token for
the system it syncs, a customer's account number, a webhook secret. The admin surface stores
three of them (an execution's `input`, a manifest's `properties`, a work queue entry's `input`)
and an effect factory's settings are the same kind of thing. Each is served by a single-row
detail read under the `operations` namespace (`executionDetail`, `manifestDetail`,
`workQueue.detail`, and `effects` for the settings), never by a list type, and nothing else
gates them. Whoever passes the operations gate can read one; nobody can page through all of
them.

## Status

**Accepted.**

## Considered options

**Put the input on the summary types.** The parity audit that added the manifest and work queue
reads asked for `WorkQueueSummary.input`, and it is the obvious edit: the dashboard's detail page
shows the input, and the point read `workQueue(id)` already returns `WorkQueueSummary`. It loses
because the same type is what `workQueues` returns, so the field would be on every row of every
page. A caller holding the operations credential could then walk a million rows of inputs with
keyset paging, which is a bulk export of every secret the scheduler holds, and each page would
read the payload column whether or not the client selected it.

**A per-field authorization rule.** A second policy on the input fields, stricter than the
namespace, would let a host hand out an operator credential that sees topology but not payloads.
Nothing in Trax asks for that split today, and adding it means a second vocabulary beside
`GateOperations`, the thing [0004](./0004-the-operations-namespace-gates-independently-of-the-endpoint.md)
declined to invent. If a host needs it, the detail reads are the only fields it has to cover,
which is the point of keeping them few.

**Redacting known secret shapes.** Rejected: Trax cannot know which property of a host's input is
a secret, and a redactor that guesses gives a false sense of cover.

## Consequences

**A frontend makes two calls where it might have made one.** The list shows the row, the detail
read shows the input. Execution detail already worked this way, so the React dashboard's pages
were built around it.

**A new list type must not grow an input field.** That is the rule the guard below holds.

## Exemplars

- `TrainInputReadsTests` asserts the three summary types carry no input field and the three
  detail types do, and sends all four input-bearing reads through a host gated with
  `GateOperations(roles: "Admin")` as an anonymous caller and as an authenticated caller
  without the role: both are refused.

Not covered: the effects list does carry settings, because there is one row per registered
factory and no paging, and nothing checks that a new query elsewhere in the schema does not
return a train input by another name.

## Changelog

- **2026-09-27**: Recorded.
