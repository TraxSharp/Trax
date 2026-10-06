---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, api, dashboard, samples]
areas: [platform]
status: accepted
---

# A chain declares every track, and a decider chooses one per run

A train may take different junctions on different runs, but only along tracks its chain
declares. `Switch`, `Gate` and `Scale` name every track in `Junctions()`; the choice between
them is made at run time by an `IDecider`, from typed questions about a value in Memory. The
chain is still one declaration that the host verifies at startup, and the decider can only
answer with a track the chain offered.

## Status

**Accepted.** Refines [0016](./0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md),
which still holds: `Junctions()` cannot branch on its input. What it gains is a declared way to
branch whose every path is visible to the check.

## Why this is written down

Because it looks like it contradicts 0016, and it is the only way to branch that does not.

0016 refuses a chain that branches on its input because such a chain has several shapes and
the check reads only one. A switch has several paths but one shape: every track is recorded
when the chain is read, and the replay verifies each of them from the Memory at the switch. A
junction after the switch can rely only on what every track produces. Nothing a run decides
can reach a path the check did not see.

Typed decision models (one state and typed questions in, calibrated probabilities out, no
generated text) made this worth doing now. A model that picks one of a fixed set of options is
exactly the shape a declared choice needs, and the decider interface keeps Trax independent of
any one of them: a rule table, a classifier or a test double answers the same questions.

## Considered options

**Branch inside a junction, and run the chosen path as a separate train through the bus.**
Works without changing Core, and each path is verified as its own train. Rejected as the
primitive because the parent's declared chain then says nothing about its paths, and the
decision itself (which model, how confident, why it was overruled) has nowhere to be recorded.
It remains a fine way to run a path that is a train in its own right.

**Branch on the input, and verify every shape the reader can find.** Rejected for the reason
0016 gives: the reader cannot find shapes it has no way to enumerate.

**A decider typed per decision (`ISelector<TState, TTrack>`).** Rejected because it cannot ask
several questions in one call, which is how decision models are priced and how they answer
fastest, and because a model adapter would have to be registered once per question type.
`IDecider` takes the questions as data, the way the models' wire format does, and the typed
surface lives in the chain.

## Consequences

**A decision that cannot be trusted fails the run.** An answer naming an option that does not
exist, a probability outside 0 to 1, a score off the scale or a missing answer fails it, with
the step named and the failure classified transient, because a model asked again usually answers
properly. A declaration that cannot work, and a decision the declaration gives no track to take,
are classified permanent. A decider that throws fails it too: an error is not a decision, so no
fallback track is taken on its behalf. A choice below a track's confidence bar takes the declared
`Otherwise` (or `Unsure`) track, or fails the run when none is declared, so a train whose outcomes
all matter can refuse to guess.

**The question is part of the declaration.** A model is meant to judge by the instructions and
each option's description, not the type's name, so a decision with no question (`[Asks]` or
`asking:`) is refused when the chain is read. The type's name is not hidden either: a question is
keyed by the type's name without its namespace (`QuestionKey.For`, generic arguments in angle
brackets), or by a `Key` set on `[Asks]`, and a model adapter may send that key as the question's
id, as the System One adapter does. The key is short because a model reads it and because it is
part of every recorded answer's fingerprint: moving a type to another namespace must not change
what the model sees or stop a requeue replaying. The cost is that two types sharing a name can
collide, so one train asking about two types under one key is refused at startup, and `Key`
separates them.

**A decider can refuse a declaration at startup.** A decider that implements `IVetsQuestions` is
shown each step's declared questions when the chain is read, and every problem it names refuses
the chain, so a question its model can never answer stops the host rather than failing every run.

**The live decider is handed the train's state; a shadow gets a copy.** The live decider reads the
object in Memory and must not change it. Each shadow gets its own copy, written to JSON and read
back, so nothing a shadow does reaches the run or another shadow; a state type JSON cannot copy is
refused at startup when shadows are declared on it. A shadow from the container is built in a DI
scope of its own, so it never shares scoped services with the live decider or the junctions after
it, and a shadow that ignores cancellation has that scope disposed a second later.

**Memory is keyed by type, so a decision has a type.** A choice is `ChoiceDecision<TTrack>`, a
score `ScoreDecision<TLevel>`, a yes/no `YesNoDecision<TQuestion>` for a marker type, and the
track taken `TrackTaken<TKey>`. Two decisions about the same type in one run overwrite each other.

**Recording and replay are hooks, not Core features.** Core reports every decision and routing
to an optional `IDecisionObserver`, and asks an optional `IDecisionReplay` before it asks a
decider. The observer is awaited before any track is taken, and is best effort unless it declares
itself `Required`, in which case a failure to record fails the step. A live answer the run
refuses is reported to the observer too, so the record shows what the decider said even when the
step failed on it. Each asking of a question
has a fingerprint, a hash of the step and the question's declaration and never of the state, and
a replay hands back the fingerprint an answer was recorded under. A replayed answer whose
fingerprint differs, or that no longer fits its question, is not acted on: the decider is asked
afresh and the reason reported. Trax.Effect implements both; see
[0041](./0041-a-requeued-run-replays-the-decisions-of-the-run-it-repeats.md).

## Exemplars

**Enforced elsewhere:** `DecisionTests` in Trax.Core pins that only what every track produces
is available after a routing step, that a fault inside a track is reported at the routing step
naming the track, that routing on a decision nothing made, a missing decider and an unsupplied
shadow are reported, that each declaration mistake is refused (a track declared twice, no
tracks, a confidence outside 0 to 1, no question, a question asked twice, a gate with no Yes
track or crossing bars, a scale with nothing for its lowest level or fewer than two levels, a
track on a value the enum does not define, a track naming a type that is not a junction), that
reading the chain asks no decider and runs no track, that a step checks its tracks before it
asks, and that an unfit or missing answer fails the run as transient while a declaration that
cannot work stays permanent, and that shadows on a state type JSON cannot copy are refused.
`DeciderTests` in Trax.Core pins that a cascade escalates an answer that does not fit its
question. `QuestionKeyTests` pins the short key, an explicit `Key` and its limits, and the refusal
of two types asked about under one key. `QuestionVettingTests` pins that a decider implementing
`IVetsQuestions` refuses a declaration when the chain is read, and that a cascade vets with each
tier. `DecisionExampleTests` in Trax.Core runs three applications
end to end: triage with a fallback and a per-track bar, underwriting with no fallback, and
moderation asking three questions in one call and routing on all of them.
`DecisionRuntimeTests` in Trax.Core pins that a shadow that never answers does not hold up the
run, that a shadow is not asked a replayed question, that an unregistered shadow is refused at run
time as at startup, that shadow agreement on a `Switch`, `Gate` or `Scale` is taking the same
track and is never agreement when the live answer takes no track, that a shadow from the
container shares no scoped service with the live decider, that each shadow is handed its own
copy of the state, that a hung shadow's scope is disposed after cancellation, that every refused
live answer reaches the observer's `Refused` and is named in the failure, that a replayed answer that no longer
fits, or whose fingerprint differs because the question was reworded or another asking was
inserted ahead of it, is asked afresh, that a recorded choice of a member with no track replays
and takes `Otherwise` again, that a replay that throws fails the step, that a `Required`
observer's failure fails the step before any track, and that a cancelled run asks, tells and
routes nothing.

Not covered: nothing checks that a question's words say what its author meant, and a model's
calibration is the model's own; the thresholds a train declares are only as good as the
labelled cases they were tuned on.

## Changelog

- **2026-10-02**: A decider's unfit or missing answer is transient, not permanent, and reported
  to the observer as a refusal; a replay is matched to its asking by a fingerprint; question keys
  drop the namespace and can be set with `[Asks(Key)]`, with collisions refused; a decider can vet
  declared questions at startup; each shadow gets its own copy of the state and its own DI
  scope.
- **2026-10-01**: Question keys are the type's full name and may reach a model as the question's
  id; the observer is async and can be required; a replayed answer that no longer fits is asked
  afresh rather than failing the run.
- **2026-10-01**: Recorded.
