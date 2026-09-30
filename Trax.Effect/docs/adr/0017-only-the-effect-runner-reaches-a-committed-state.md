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
effect's target unless the stored draft is already there (`state-reserved`), and a stored draft it cannot read may
only be reset to the initial state (`draft-unreadable`), never overwritten.

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

## Consequences

A client drives an effect-bound transition with the send mutation, never the advance mutation. A test that needs a
draft in a committed state seeds it through the store rather than through autosave. A stored draft that fails
rehydration, for example one written under a newer definition during a rolling deploy, can be read again once the
definition catches up, or reset; autosave does not overwrite it in the meantime.

## Exemplars

- `DraftWriteRulesTests` pins each rule: the advance refuses an effect-bound trigger, the effect runner still fires
  it, autosave refuses to create or enter a committed state or an effect's target, keeps a same-state save, and
  refuses to overwrite an unreadable draft except with a reset.
- [Persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports) is the rule this produces.

Not covered: a machine author can still declare a transition into a committed state that binds no effect, and the
advance fires it like any other; the rule is about the effect's edge and the states marked committed, not about
every state an author considers final.

## Changelog

- **2026-09-29**: Recorded.
