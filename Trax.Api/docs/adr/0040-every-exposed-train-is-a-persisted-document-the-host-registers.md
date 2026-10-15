---
authors: [Theauxm]
areas: [graphql]
status: accepted
---

# Every exposed train is a persisted document the host registers

A host registers one persisted document for every train it exposes with `[TraxMutation]` or `[TraxQuery]`, as it
already did for every exposed machine: generated from its schema at startup, under the reserved prefix `trax_train_`,
with an id that carries a hash of the train's shape. A client sends the id, never the document.

## Status

**Accepted.**

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

**What refuses.** A document selects the whole answer, a union or interface through a fragment per possible type. An
answer that holds itself, or a field that takes a required argument, has no finite document, so it refuses the host's
start, naming the train and the type.

**The first rolling deploy.** A node from before this decision does not treat `trax_train_` as reserved: given a new
id it looks the id up in the store and runs the new document against its own schema, so a client may get one
validation error while old and new nodes overlap. It happens once, during the deploy that introduces the prefix.

Weighed and rejected: keeping hand-written train documents (the drift above), and a document per train only when a
frontend asks for one (the host cannot know, and the manifest would no longer list what the host serves).

## Exemplars

- `TrainDocumentsE2ETests` pins that every exposed train is registered and runs by id, that an id of another shape is
  answered as outdated with a 4xx, and that the manifest lists each train with what it requires.
- `TrainDocumentsTests` pins the id grammar, that the shape hash moves with an input field and not with a description
  or another train, the selection, and the refusal of an answer that holds itself.
- `PersistedOperationMutationTests.Upload_deactivate_and_restore_refuse_the_reserved_prefix` pins that the prefix
  cannot be written through the management surface.

Not covered: the overlap of the first rolling deploy, which no single-version test can run.

## Changelog

- **2026-10-14**: Recorded.
