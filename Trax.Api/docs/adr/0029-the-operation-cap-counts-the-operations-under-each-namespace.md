---
authors: [Theauxm]
areas: [graphql]
status: accepted
---

# The operation cap counts the operations under each namespace

`MaxOperationsPerRequest` (50 by default) bounds the operations one GraphQL request invokes. An
operation is a root field or a field under a namespace: `dispatch`, `discover`, `operations`,
the namespaces nested in them (`operations { deadLetters }`, `operations { workQueue }`), and the
namespace a train or query model declares. A namespace field is not an operation itself; the
fields under it are. Aliases and batched operations count, fragments count as if written in
place, and selections sharing a response path count once because they merge at execution.

Every train and admin field sits under a namespace, so that is where the cap counts:
`mutation { dispatch { a: t b: t c: t } }` is three operations. The cap exists to bound how many
trains and admin actions one request runs.

## Status

**Accepted.**

## Considered options

**A separate cap per namespace.** It adds a second number a host has to understand and tune,
and a request could still spread its work across several namespaces to reach the sum of the
caps. One cap over everything the request invokes is one sentence.

**HotChocolate's cost analysis.** It is on by default and does bound large requests, but it
prices fields, not invocations: its limit moves with every resolver's assigned cost and with
list sizes, so it cannot state "at most 50 train runs". The cap is a Trax validation rule beside
it, and both apply. HotChocolate offers no built-in limit on aliases or on fields per selection
set (`MaxAllowedFields` bounds the whole document).

**Recognising namespaces by name.** A fixed list of field names misses the nested and declared
namespaces. Each namespace field is marked when the schema is built (`NamespaceField`), and the
rule walks the document against the schema, descending through marked fields only.

## Consequences

**A field a host adds through a type extension is counted as an operation**, even when it
returns an object of further fields. The cap errs toward counting more.

**The rule is registered through HotChocolate's validation builder** (`AddValidationRule`), so it
runs during validation, before any resolver, on every transport.

## Exemplars

- `OperationCapUnderNamespacesTests` pins it end to end: 51 aliased train calls under
  `dispatch` are refused at the default cap and 50 are not; calls under a declared namespace,
  under an aliased namespace field, through a fragment, under `operations { deadLetters }` and
  under `discover` all count; a repeated response path counts once.

Not covered: the rule's fragment and cycle handling is pinned by the unit tests in
`GraphQLHardeningTests.cs`, which are not ADR guards.

## Changelog

- **2026-09-30**: Recorded.
