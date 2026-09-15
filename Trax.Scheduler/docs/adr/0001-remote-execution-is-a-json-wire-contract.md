---
authors: [Theauxm]
areas: [scheduling, platform]
status: accepted
---

# Remote execution crosses the process boundary as JSON, not as a compiled type

A run dispatched to a remote worker travels as `RemoteRunRequest(TrainName, InputJson,
InputType)`: the train's service type name, the input serialized to JSON, and the input's
type name. The asynchronous half travels as `RemoteJobRequest(MetadataId, Input, InputType)`
and carries no train name at all, because the far side loads the metadata row that
`MetadataId` names and takes the train from it. Nothing about the train's compiled type
crosses the boundary either way.

## Status

**Accepted.**

## Considered options

**Binary serialization of the input object.** Faster and type-safe at both ends, and
rejected because both ends would then have to ship the same assemblies at the same version.
That is exactly the coupling remote execution exists to avoid: a Lambda runner should be
deployable without redeploying the scheduler.

**Sending the concrete train type.** Rejected for the reason
`Trax.Docs/adr/0007-the-canonical-train-name-is-the-interface-fullname.md` gives: the
concrete type is an implementation detail that can be renamed or swapped, while the
interface FullName is the identifier every other layer already stores.

## Consequences

**The contract is a public record, and changing it is a breaking change for anyone running
a worker built against the old shape.** Adding a property is safe; renaming or removing one
is not, and neither end validates a version.

**`InputType` is read only on the job path.** `TraxRequestHandler.ExecuteJobAsync` resolves
it to a `Type` to deserialize a `RemoteJobRequest`'s input, because at that point the handler
has only a `MetadataId`: the train, and with it the declared input type, is not resolved
until `JobRunnerTrain` loads the metadata row. The run path never reads it: `RunTrainAsync`
hands `TrainName` and `InputJson` to `ITrainExecutionService.RunAsync`, which deserializes
against the input type the train's own registration declares. A worker that cannot satisfy a
run therefore fails with `TrainNotFoundException`, not with a deserialization error naming a
type. That exception deliberately does not name the train either: its message is a constant,
because enumerating registered trains through error text would let an unauthenticated caller
probe the API surface, and the name it was asked for is kept on the exception for logging
only. `RemoteRunRequest.InputType` is written by both executors and read by
nobody; it stays because dropping a property from a deployed contract is its own decision.

**`TrainName` resolves in two tiers, and the wire accepts either.** `FindTrain` matches
`ServiceType.FullName` first and falls back to the short `ServiceTypeName`. The fully
qualified name is what `work_queue.train_name` and `manifest.Name` store, so a remote run and
a local one describe the same job, and it is what the only production caller sends:
`TrainExecutionService.RunAsync` passes `registration.ServiceType.FullName` to
`IRunExecutor`, so no production path can put a free-form string on the wire. A short name
sent by hand resolves anyway when it is unique, and throws `AmbiguousTrainNameException` when
it is not. What goes unchecked is the tier, not the spelling: nothing rejects a short name,
so the canonical-name rule
(`Trax.Docs/adr/0007-the-canonical-train-name-is-the-interface-fullname.md`) is a convention
the resolver is lenient about rather than one this repo enforces.

**On the Lambda topology a run is not a bare `RemoteRunRequest`.** A direct invoke has no URL
path to route on, so `LambdaRunExecutor` serializes the request to a string and wraps it in
`LambdaEnvelope(Type, PayloadJson)`. That is a second wire shape with the same missing
version as the first, and `LambdaRequestType` crosses as its integer ordinal, so the order of
its members is part of the contract too.

## Exemplars

- `RemoteRunContractTests` round-trips the run request, the run response and the job response
  through JSON and pins record equality. It also constructs two of them positionally with
  distinguishable values, so a reordering of the parameters is caught: all three of
  `RemoteRunRequest`'s strings, and positions two through four of `RemoteRunResponse`, whose
  remaining nullable strings production only ever builds by name. Reordering does not change
  the JSON, which binds by name; it swaps the values the positional call sites hand over. A
  property rename is caught by the compiler here rather than by the round-trip.
- `HttpRunExecutorTests` captures the request `HttpRunExecutor` actually put on the wire and
  asserts each of the three strings landed in the property it belongs to.
- `LambdaRunExecutorTests` decodes both layers of the Lambda payload, the envelope and the
  `RemoteRunRequest` inside it, so a change to either shape fails before an invoke does.

Not covered:

- Nothing checks that the two ends agree on a version, because there is no version field. A
  worker built against an older contract and a scheduler sending a newer one will
  deserialize whatever matches and silently drop the rest.
- Nothing pins the wire encoding of `LambdaRequestType`. Both sides of every test share the
  same enum, so reordering its members changes the integer on the wire and the tests stay
  green.

## Changelog

- **2026-09-12**: Corrected the `TrainNotFoundException` claim (its message never names
  the train, by design) and the exemplar description of what the positional tests pin.
- **2026-09-11**: Corrected `InputType` (read only on the job path, dead on the run path) and
  `TrainName` resolution (two tiers, and the production caller cannot supply a free-form
  string). Retitled, because the asynchronous half is keyed by `MetadataId` and carries no
  train name. Added the Lambda envelope and the enum encoding it leaves unpinned.
- **2026-09-11**: Recorded.
