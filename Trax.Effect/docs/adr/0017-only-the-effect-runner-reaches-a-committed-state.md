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
(`draft-committed` otherwise). The effect runner records the receipt on the draft exactly as the effect loaded it,
because nothing else can write the draft while the effect runs: the effect is pending on the draft's row (central
0048, *Effects and server records*), an edit, a reset or an advance is refused as `effect-in-progress` until it ends,
and the receipt, the move to the effect's target and the end of the pending mark are one write. A machine built with the fluent builder enters an effect's target only by the effect's own edge:
`Build()` refuses any other transition into it.

The effect is not handed the request's cancellation, and an effect that ends in an `OperationCanceledException`
leaves its mark until the lease passes, because a cancellation says nothing about whether the effect happened; past
the lease the effect is run again under its key and its result recorded. A draft with a pending effect is neither
deleted nor reset.

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

**Refusing autosave while the draft's effect is in flight.** Rejected at first in favour of the token check, then
adopted (2026-10-13). The token check kept the receipt off a draft written during the effect, but left the effect's
result in the claim ledger alone, and every other path then had to work out from the ledger whether the result had
reached the draft. The cost is the one first given: a user cannot edit a draft whose send has stalled, until the
lease passes and the effect is finished. Reading the ledger on every save is no longer needed, because the mark is on
the draft's row.

**Committing the content the effect ran on over a later edit.** Not adopted, neither at the first send's commit nor
at a later replay. It would record exactly what was charged, but it silently discards a write the client was told
had been saved, and at replay it needs the claim to hold the whole content rather than a fingerprint of it.

**Replaying the receipt onto the draft as it is at the next send.** What the runner did until the claim carried a
fingerprint. Rejected: the effect runs once, but the committed state then pairs its receipt with content it was not
produced for, and nothing tells the client the two differ.

**Running the effect again for the edited content.** Rejected: the effect is irreversible and has already happened
once for this draft, which is the one thing the claim exists to prevent.

**Refusing the replay when the content differs (`draft-changed`).** Adopted, and retired for machines on
2026-10-13: with writes refused while the effect is pending, the content cannot differ. It is the fail-closed choice: nothing
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
initial state has no soft reset out of it. A write to a draft while its effect is pending is refused as `effect-in-progress`, so a client sends nothing else
for that draft until the send settles, or until the lease passes and the effect is finished for it. An effect is run
again under its key when its claimant stops without recording a result, so an effect passes its key to the system it
calls. A completed claim from the earlier ledger whose draft is still in the effect's state is recorded by the
draft's next send. An effect that honours the
request's cancellation after its irreversible step no longer has a token to honour.

## Exemplars

- `DraftWriteRulesTests` pins each rule: the advance refuses an effect-bound trigger, the effect runner still fires
  it, autosave refuses to create or enter a committed state or an effect's target, refuses to rewrite a draft already
  in one (committed, or an effect's target) or to reset it where the machine declares no reset, accepts a save that
  changes nothing, and refuses to overwrite an unreadable draft except with a reset.
- `EffectCommitIntegrityTests` pins the runner's half: an edit or a reset during the effect is refused, the receipt
  and the move are one write, a cancelled effect keeps its mark until the lease passes, and the effect never sees the
  request's token. The pending mark's own rules are pinned by `EffectPendingTests`.
- `MachineBuilderTests.Build_refuses_a_second_edge_into_an_effects_target_state` pins the build-time refusal.
- [Persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports) is the rule this produces.

Not covered: a machine author can still declare a transition into a committed state that binds no effect, and the
advance fires it like any other; the rule is about the effect's edge and the states marked committed, not about
every state an author considers final. A machine defined directly as a `MachineDefinition`, not through the fluent
builder, is not checked for a second edge into its effect's target.

## Changelog

- **2026-10-13**: Amended with central 0048. The effect is pending on the draft's row, and every other write to the
  draft is refused until the receipt and the move are written together; past its lease the effect is run again under
  its key. The token check, the content fingerprint, `draft-changed` and a reset's release of settled claims are
  retired for machines, since nothing can write the draft between the effect and its record. Refusing writes while
  the effect is in flight, rejected on 2026-09-30, is adopted.

- **2026-10-12**: The 2026-10-09 entry states the generic save's refusal as central 0048 does: a gated rule
  (`Requires` or `RequiresPolicy`) on an event or the start, not only a `Requires` rule.
- **2026-10-09**: Amended by central ADR 0048 (`Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md`).
  Autosave is no longer how a client moves a machine. The typed per-machine surface has no save: a draft starts with
  an explicit `start` and changes only by declared events, and an effect-bound event is an ordinary event whose
  mutation runs the effect. The generic save survives only behind `ExposeGenericStateMachineMutations()`, and there
  it serves only machines with no server-side behaviour: a machine with a gated rule (`Requires` or `RequiresPolicy`)
  on an event or the start, a timer, an invoked train or an effect answers `save-not-allowed`. A timed state is locked: autosave neither leaves nor rewrites it. The rules
  above still hold for the effect runner and for host code that writes through the store.
- **2026-09-30**: Amended. The claim records a fingerprint of the content its effect ran on (Postgres migration 053,
  SQLite 018), and a send replays the receipt only onto that content, refusing any other as `draft-changed`. Claims
  without a fingerprint replay as before.
- **2026-09-30**: Amended. The rule covers the content as well as the state: autosave no longer rewrites a draft in
  a committed state or an effect's target, the runner commits only on the draft its effect loaded, a reset keeps a
  claim whose outcome is not yet on the draft, a cancelled effect keeps its claim until the lease passes, and
  `Build()` refuses a second edge into an effect's target.
- **2026-09-29**: Recorded.
