---
authors: [Theauxm]
repos: [mediator, dashboard, scheduler, api]
areas: [platform, graphql]
status: accepted
---

# Caller-supplied train input is read case-insensitively and duplicate properties are refused

JSON a caller supplies as a train's input is matched to the input type's properties without regard
to case, and a property that appears twice, in the same or any other casing, is refused with a
`JsonException` rather than resolved to one of its values. The API (through
`ITrainExecutionService`) and the dashboard's Run dialog read the same JSON the same way.

## Status

**Accepted.**

## Considered options

**Exact case, as the system options read it.** What the enqueue did. A property cased differently
from the camelCase the options expect was not an error: it was skipped, and the train queued with
that property's default. The dashboard read case-insensitively, so one JSON document could mean two
different inputs depending on where it was typed, and neither surface said anything.

**Case-insensitive, duplicates allowed.** System.Text.Json's default for a repeated property is to
keep the last value. Once casing is ignored, `{"amount":1,"Amount":999}` names one property twice,
and a reviewer or a validating proxy that reads the first value would approve an input the train
then runs with the second. Refusing the ambiguity (`AllowDuplicateProperties = false`, new in
System.Text.Json 10, which counts case-insensitive matches as duplicates) costs nothing for honest input.

**Refusing unknown properties instead.** Would also have caught the mis-cased property, but breaks
every caller sending a field an older input type does not have, and says nothing about duplicates.

## Consequences

This applies to input a caller or operator writes. JSON Trax writes and reads back itself (a work
queue entry's stored input, manifest properties, metadata input) keeps the system options: it is
always camelCase, and reading it differently would change nothing.

A dictionary inside an input keeps case-sensitive keys: `PropertyNameCaseInsensitive` matches
properties only, so `{"a":1,"A":2}` in a `Dictionary<string, int>` is two entries, not a duplicate.
The same key given twice exactly is still refused.

## Exemplars

**Enforced elsewhere:** `TrainInputCasingTests` in Trax.Mediator (any casing is matched, and a
property given twice, in the same or a different case, is refused by `QueueAsync` and `RunAsync`).

A surface that reads caller input without queueing or running through the mediator gets these
settings from `ITrainExecutionService.PrepareAsync` instead of repeating them.

Not covered: the dashboard's Run dialog reads case-insensitively but does not refuse duplicates
yet, and nothing checks that a new surface reading caller input uses the same settings.

## Changelog

- **2026-09-27**: `PrepareAsync` is how another surface reads caller input the same way.
- **2026-09-27**: Recorded.
