# Interaction matrix

A new kind of chain step has to work with everything a train already does: a requeue has to
replay its decisions, a cancel has to reach inside it, its junction events have to carry the
right node. Each of those is easy to break in a step kind nobody thought to test against it.
This table is where a new step kind says how it behaves with each existing feature, and
which test proves it.

**Rows** are step kinds added after `Chain`, `IChain`, `ShortCircuit`, `Extract`, `Resolve`,
`Seed`, `Decide`, `Switch`, `Gate` and `Scale`. **Columns** are the existing features. **Each
cell** names the test that covers the pair, as `` `ClassName.MethodName` ``, a class under some
folder's `tests/`. Where a pair genuinely cannot interact, the cell says
`n/a: <why it cannot>` instead; that is an answer, not a gap.

`InteractionMatrixTests` in `Trax.Core.Tests.Meta` reads this file. It fails when a row has an
empty cell, when a cell names a test that does not exist, and when `ChainStepKind` gains a member
that has no row here.

The columns:

- **Decision replay**: a requeued run replays the recorded decisions (`Trax.Docs/adr/0041`).
- **Ask afresh**: a replay that asks the decider again instead of reusing the answer.
- **ShortCircuit**: an earlier step short-circuits the train.
- **Requeue**: the run is requeued from the dashboard or the API.
- **Dead letters**: the run fails into the dead-letter table and is retried from there.
- **Cross-host cancel**: a cancel requested on another host reaches the running step.
- **Dashboard cancel**: a cancel requested from the dashboard.
- **Job timeout**: the scheduler's job timeout expires mid-step.
- **Junction events**: the events `AddJunctionEvents` emits, with the right node id.
- **Progress**: the junction progress provider reports the step.
- **Sensitive and withheld**: `[TraxSensitive]` data and withheld tracks stay masked.
- **AddServices before the fork**: services registered before the step are visible inside it.
- **Same junction twice**: one junction type used twice in the chain.
- **Tuple outputs**: a junction returning a tuple that the step consumes.
- **Same Switch enum twice**: two routing steps on the same enum.
- **Remote workers**: the run executes on a remote, Lambda or SQS worker.
- **Retry backoff**: the run is retried with backoff after a failure.

| Step kind | Decision replay | Ask afresh | ShortCircuit | Requeue | Dead letters | Cross-host cancel | Dashboard cancel | Job timeout | Junction events | Progress | Sensitive and withheld | AddServices before the fork | Same junction twice | Tuple outputs | Same Switch enum twice | Remote workers | Retry backoff |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
