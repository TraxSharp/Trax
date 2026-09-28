---
authors: [Theauxm]
areas: [platform, data-model]
status: accepted
---

# A sensitive field is marked, and masked where its copy is written

A member of a train's input or output marked `[TraxSensitive]` is written as `{"_redacted": true}`
in every copy Trax keeps for people and tools to read: the stored input and output
(`SaveTrainParameters`), the junction output the junction logger records, and the output handed to
lifecycle hooks. Those copies reach the dashboard, the API's execution detail, logs and
subscriptions, and before this the only control was dropping a train's whole input or output
(`ExcludeInput` / `ExcludeOutput`). Marking is opt-in, and nothing is masked because of its name.

## Status

**Accepted.**

## Considered options

**Masking by name as well (`password`, `token`, `secret`, `apiKey` and the like), on by default.**
Rejected. A name list is a guess that looks like a guarantee: it misses `pin`, `cvv`, `ssn` and
every domain-specific field, and it masks a `tokenCount` or a `secretSantaId` that someone needs
to read. A host that trusted the defaults would have unmarked secrets stored with nothing telling
it so. The attribute makes the author of the type say what is sensitive, where the type is
declared.

**Masking when the copy is read (in the API and the dashboard) instead of when it is written.**
Rejected: the value would still be in the database, in logs and in every broadcast, and each
reader would have to remember to mask. Masking where the copy is written covers every reader at
once.

**Dropping the member from the JSON instead of writing a stand-in.** Rejected: a missing member
reads as "not set", and nothing downstream could tell a masked input from a complete one. The
stand-in is an object, like the `{"_truncated": true}` the byte ceiling already writes, so
`TraxRedaction.ContainsRedaction` can find it anywhere in the tree and a re-queue can refuse an
input that no longer holds what the run was given.

## How it works

`TraxRedaction.WithRedaction(options)` derives, once per options instance, serializer options
whose type-info resolver gives each marked member a converter that writes the stand-in. The
original options are untouched, and the train runs with the real value. Because it is a resolver
modifier rather than a walk of the object, it applies wherever System.Text.Json goes: nested
objects, each element of a collection, a value behind an `object`-typed member, and a member
renamed by `[JsonPropertyName]` (the JSON name is kept). A marked member hides its whole value,
so an object under it is not walked.

## Consequences

A masked input cannot be read back as the input the run had: reading the stand-in throws. The
dashboard's and the API's "re-queue this run from its saved input" need to refuse such an input,
as they already refuse a truncated one.

The copies a train is *run* from are not masked, because the train needs the real value: a queued
entry's input (`work_queue.input`) and a manifest's properties. Those are transport, not a record,
and protecting them is a question of what is put in an input at all.

## Exemplars

- `TraxRedactionTests` pins each shape: a nested object, collection elements, a whole marked
  collection or object, a positional record parameter with and without `property:`, a renamed
  member, a field, a value type, an override, an interface declaration, and that no member is
  masked for its name.
- `ParameterEffectTests.cs`, `JunctionLoggerProviderTests.cs` and `SensitiveOutputHookTests.cs`
  check the stored input and output, the junction logger's output and the hook output.

Not covered: nothing finds a sensitive member left unmarked, which is the cost of opt-in. A value
a train writes into its own log messages, or into an exception message, is not a serialized
member and is not masked.

## Changelog

- **2026-09-27**: Recorded.
