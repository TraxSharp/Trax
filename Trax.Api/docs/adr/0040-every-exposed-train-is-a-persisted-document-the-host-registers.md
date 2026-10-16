---
authors: [Theauxm]
areas: [graphql]
status: deprecated
---

# Every exposed train is a persisted document the host registers

A host registers one persisted document for every train it exposes with `[TraxMutation]` or `[TraxQuery]`, as it
already did for every exposed machine: generated from its schema at startup, under the reserved prefix `trax_train_`,
with an id that carries a hash of the train's shape. A client sends the id, never the document.

## Status

**Deprecated.** A host no longer generates a document for a train: a frontend writes every document that reaches
one, per the central [ADR 0054](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0054-a-frontend-writes-every-document-that-reaches-a-train.md).
A select-everything document grows with the number of paths through the answer, so an EF model whose entities
reference each other either stopped the host at the parser's field limit or left the train refused for cost on
every call. The `trax_train_` prefix stays reserved so earlier ids are answered as outdated.

## Why this is written down

Before, a frontend that called a train wrote its document by hand, versioned its id by hand and uploaded it. A train
whose input or output changed failed an old client with a GraphQL validation error, which no client can tell from a
bug. With the documents generated, a train in another shape answers `CLIENT_OUTDATED` (reload) or `SERVER_OUTDATED`
(retry), as a machine does, and `trax client` reads the ids from the host's [client manifest](https://traxsharp.net/docs/sdk-reference/statemachine-api/client-manifest).

**What it costs.** The persisted-operation allow-list now holds every exposed train, whether or not any frontend
calls it. With `RequirePersisted`, the allow-list no longer narrows a host to the operations its frontends use; it
refuses shapes the host did not generate. Exposure is still decided by `[TraxMutation]` and `[TraxQuery]`, and each
train's own authorization still runs; a train no client should call is not exposed.

**The id.** `trax_train_<root>_<namespace>_<field>_<shapeHash><textHash>`, with a `_` in a GraphQL name written `-`
so the segments always read back. The shape hash covers the field's signature and the SDL of every type the document
reaches, without descriptions and without the root and namespace types that hold every other train: a new input
field moves the id, a doc comment or another exposed train does not. Versions are numbered per slot (the id without
its hash), as a machine's are per machine.

**What is left out, and what refuses.** A document selects the whole answer, a union or interface through a fragment
per possible type, except a field that leads back to a type already on the path: an entity's reference to its parent,
a tree's children. That field is left out, a paged one whole (a connection counts as its node type), and the host logs
each one it left out at startup. The schema may hold the cycle; the document never follows it. Refusing the train
instead, as this record first decided, stopped every host that exposes an EF model with navigations both ways, which
is the ordinary shape of one, and the alternative of a type per train the consumer writes only to flatten the model
was rejected. A field that takes a required argument still refuses the host's start, naming the train and the field:
nothing gives the document a value for it. Every generated document, a machine's, a train's or the progress pair, then
runs through the validation the host runs on a request (its depth limit, the operation cap, Trax's rules) before it is
registered or exported, and a document it refuses refuses the start, naming its owner: a deep answer is a boot failure,
not a document that fails every call. Cost is analysed per request, not in validation, so it stays a run-time check.

**The first rolling deploy.** A node from before this decision does not treat `trax_train_` as reserved: given a new
id it looks the id up in the store and runs the new document against its own schema, so a client may get one
validation error while old and new nodes overlap. It happens once, during the deploy that introduces the prefix.

Weighed and rejected: keeping hand-written train documents (the drift above), and a document per train only when a
frontend asks for one (the host cannot know, and the manifest would no longer list what the host serves).

## Exemplars

**Unenforced:** the decision is withdrawn; nothing in the code follows it any more, and the guards that pinned it were
removed with it. ADR 0054's exemplars pin what replaced it.

## Changelog

- **2026-10-16**: Deprecated: generated train documents are removed (central ADR 0054).
- **2026-10-16**: A field leading back to a type already on the path is left out instead of refusing the train, and
  every generated document is validated against the host's rules before it is registered.
- **2026-10-14**: Recorded.
