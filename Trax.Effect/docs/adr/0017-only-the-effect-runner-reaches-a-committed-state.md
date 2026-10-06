---
authors: [Theauxm]
areas: [platform, data-model]
status: accepted
---

# Only the effect runner puts a draft into a committed state or an effect's target

A state-machine draft has two write paths besides the effect runner: the soft path (autosave), which stores a
snapshot the client computed, and the authoritative advance, which fires one trigger on the stored draft. Neither
creates a committed state. The advance refuses a trigger bound to the machine's effect from the stored state as
`effect-bound`; the effect runner fires it, through an internal overload, only after the effect has run and with
the effect's receipt as its input. Autosave refuses to create a draft in, or move one into, a committed state or an
effect's target (`state-reserved`), and a stored draft it cannot read may only be reset to the initial state
(`draft-unreadable`), never overwritten.

The rule covers the draft's content, not only its state. Autosave does not rewrite a draft already in a committed
state or an effect's target: the only write it accepts there is a reset to the initial state that the machine itself
declares from that state, and a save identical to the stored draft is answered as saved without writing
(`draft-committed` otherwise). The effect runner records the receipt only on the draft exactly as the effect loaded
it: it commits against that load's concurrency token, so a draft edited, reset or advanced while the effect ran is a
conflict rather than a commit, and the claim keeps the receipt for the next send to replay instead of running the
effect again. That replay is bound to the content too: the claim records a fingerprint of the draft the effect ran on
(the SHA-256 of its canonical wire), and a send replays the receipt only onto a draft whose content still has that
fingerprint. A draft that holds other content is refused as `draft-changed`: the receipt is not recorded on it and the
effect does not run again. A machine built with the fluent builder enters an effect's target only by the effect's own edge:
`Build()` refuses any other transition into it.

What keeps the claim is part of the same rule. A reset releases a claim only once its outcome is settled on the
draft being reset: a claim in flight within its lease is kept, and a completed claim is kept unless that draft
recorded its receipt. The effect is not handed the request's cancellation, and an effect that ends in an
`OperationCanceledException` leaves its claim in flight until the lease passes, because a cancellation says nothing
about whether the effect happened. Deleting an expired draft releases its claims first and deletes the row second,
neither on the request's token.

## Status

**Accepted.**

## Considered options

**Guarding only the way out of a committed state.** What autosave already did: a committed draft may be reset or
updated in place, never moved elsewhere. Kept, but it is half the rule. A state whose meaning is "the effect ran"
is only true when the effect runner put the draft there.

**Validating the receipt in the effect's target state.** Rejected. A validator sees the shape of a receipt, not
whether the effect produced it, and the machine author would have to write that check for every machine.

**Refusing only committed states, not effect targets.** Rejected: a machine need not mark its effect's target
committed, and the target is what the receipt lives in either way.

**Refusing autosave while the draft's effect is in flight.** Rejected in favour of the token check: it closes the
same window, but a user could not edit a draft whose send had stalled until the lease passed, and it needs the
claim ledger read on every save.

**Committing the content the effect ran on over a later edit.** Not adopted, neither at the first send's commit nor
at a later replay. It would record exactly what was charged, but it silently discards a write the client was told
had been saved, and at replay it needs the claim to hold the whole content rather than a fingerprint of it.

**Replaying the receipt onto the draft as it is at the next send.** What the runner did until the claim carried a
fingerprint. Rejected: the effect runs once, but the committed state then pairs its receipt with content it was not
produced for, and nothing tells the client the two differ.

**Running the effect again for the edited content.** Rejected: the effect is irreversible and has already happened
once for this draft, which is the one thing the claim exists to prevent.

**Refusing the replay when the content differs (`draft-changed`).** Adopted. It is the fail-closed choice: nothing
is committed and nothing runs, and the client learns that the draft's action already ran on other content. Restoring
that content makes the next send replay the receipt. The fingerprint is a column on the claim, not the content
itself, so the claim stays small and holds nothing the draft does not.

## Consequences

A client drives an effect-bound transition with the send mutation, never the advance mutation. A test that needs a
draft in a committed state seeds it through the store rather than through autosave. A stored draft that fails
rehydration, for example one written under a newer definition during a rolling deploy, can be read again once the
definition catches up, or reset; autosave does not overwrite it in the meantime.

A client that autosaves the snapshot a send returned gets `Saved` without a write; one that edits a committed draft
gets `draft-committed`, and a machine with no transition from a committed state or effect target back to its
initial state has no soft reset out of it. A send whose draft was written while its effect ran reports a conflict.
Sending again replays the recorded receipt only if the draft's content is again what the effect ran on; otherwise it
is refused as `draft-changed`, and it stays refused until that content is restored or the draft expires, so a
client keeps the snapshot it sent until the send settles. A reset does not release such a claim, because the draft
never recorded its receipt. The fingerprint covers the whole canonical wire, including the definition version, so a
migration applied between the effect and the replay is also refused rather than guessed at. A claim recorded before
the fingerprint column existed has none and replays unchecked, as does every claim from a custom `IEffectClaimStore`
that does not override the fingerprint members. A reset while an effect is in flight, or after a
receipt that never reached the draft, no longer lets the next send run the effect again. An effect that honours the
request's cancellation after its irreversible step no longer has a token to honour.

## Exemplars

- `DraftWriteRulesTests` pins each rule: the advance refuses an effect-bound trigger, the effect runner still fires
  it, autosave refuses to create or enter a committed state or an effect's target, refuses to rewrite a draft already
  in one (committed, or an effect's target) or to reset it where the machine declares no reset, accepts a save that
  changes nothing, and refuses to overwrite an unreadable draft except with a reset.
- `EffectCommitIntegrityTests` pins the runner's half: an edit or a reset during the effect is not committed with
  its receipt, the next send replays it without running the effect, a cancelled effect keeps its claim, the effect
  never sees the request's token, and an expired draft leaves no claim behind.
- `EffectCommitIntegrityTests` also pins the replay's binding to content: a send after an edit during the effect is
  refused as `draft-changed` without charging again or recording the receipt, a draft restored to what the effect
  ran on replays it, an unedited draft replays it, and a claim without a fingerprint replays as before.
  The shipped-migration round trip stores and reads the fingerprint on both Postgres and SQLite, and the claim
  store's tests check that a reclaimed claim records its new owner's fingerprint.
- `MachineBuilderTests.Build_refuses_a_second_edge_into_an_effects_target_state` pins the build-time refusal.
- [Persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports) is the rule this produces.

Not covered: a machine author can still declare a transition into a committed state that binds no effect, and the
advance fires it like any other; the rule is about the effect's edge and the states marked committed, not about
every state an author considers final. A machine defined directly as a `MachineDefinition`, not through the fluent
builder, is not checked for a second edge into its effect's target.

## Changelog

- **2026-09-30**: Amended. The claim records a fingerprint of the content its effect ran on (Postgres migration 053,
  SQLite 018), and a send replays the receipt only onto that content, refusing any other as `draft-changed`. Claims
  without a fingerprint replay as before.
- **2026-09-30**: Amended. The rule covers the content as well as the state: autosave no longer rewrites a draft in
  a committed state or an effect's target, the runner commits only on the draft its effect loaded, a reset keeps a
  claim whose outcome is not yet on the draft, a cancelled effect keeps its claim until the lease passes, and
  `Build()` refuses a second edge into an effect's target.
- **2026-09-29**: Recorded.
