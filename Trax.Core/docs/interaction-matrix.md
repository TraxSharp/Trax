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
| Parallel | `PostgresParallelRunTests.Branches_asking_one_question_each_get_a_row_and_a_requeue_replays_each` | `ParallelManifestRunTests.A_dead_lettered_parallel_run_requeued_to_ask_afresh_asks_in_each_branch` | `ParallelTests.Declaration_RefusesAShortCircuitInsideABranch` | `ParallelManifestRunTests.A_dead_lettered_parallel_run_requeued_replays_each_branchs_decisions` | `ParallelManifestRunTests.A_dead_lettered_parallel_run_requeued_replays_each_branchs_decisions` | `PostgresParallelRunTests.A_run_cancelled_through_its_cancel_flag_in_one_branch_is_recorded_cancelled` | `PostgresParallelRunTests.A_run_cancelled_through_its_cancel_flag_in_one_branch_is_recorded_cancelled` | `ParallelManifestRunTests.A_parallel_run_past_its_job_timeout_is_recorded_cancelled` | `PostgresParallelRunTests.Branch_junctions_write_their_events_and_progress_side_by_side` | `PostgresParallelRunTests.Branch_junctions_write_their_events_and_progress_side_by_side` | `ParallelRunTests.A_withheld_track_in_one_branch_withholds_its_own_steps_and_everything_after_the_join_but_not_its_siblings` | `ParallelTests.AValueHandedToAddServicesBeforeTheFork_IsSeenInEveryBranch` | `ParallelTests.Declaration_RefusesOneJunctionInstanceInTwoBranches` | `ParallelTests.ATupleTheJoinNeeds_IsAssembledFromWhatTheBranchesProduced` | `ParallelTests.TheSameSwitchEnum_InTwoBranches_RoutesEachBranchOnItsOwn` | `RemoteParallelFailureTests.A_parallel_failure_crosses_the_remote_response_with_its_branch_and_combined_class` | `ParallelManifestRunTests.A_failed_parallel_run_is_retried_after_its_backoff` |

## Machine features

A state-machine feature that runs trains has to work with the same features a new step kind
does, so it gets a row too. A **row** here is a machine feature, not a chain step kind: it says
how a run the feature starts behaves with each existing feature. The columns are the step
kinds' columns and the same rules apply: each cell names the test that covers the pair as
`` `ClassName.MethodName` ``, or says `n/a: <why it cannot>`. Where the feature refuses an
existing one (an invoked run is never requeued, dead-lettered or retried), the cell names the
test proving the refusal. Where a column names a chain concept, read it for the feature: for
`Invokes`, *Same junction twice* is one train invoked from two states, and *Tuple outputs* is a
train whose output is a tuple.

`InteractionMatrixTests` checks this table as well. Core cannot see the state machine, so the
features that need a row are listed in the test (`MachineFeatures`): adding a machine feature
means adding it there, and the test then fails until the feature has a row here with the step
kinds' columns. `Invokes` stays experimental (`TRAXEXP002`) until its row is complete
(`Trax.Docs/adr/0046`).

| Machine feature | Decision replay | Ask afresh | ShortCircuit | Requeue | Dead letters | Cross-host cancel | Dashboard cancel | Job timeout | Junction events | Progress | Sensitive and withheld | AddServices before the fork | Same junction twice | Tuple outputs | Same Switch enum twice | Remote workers | Retry backoff |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Invokes | `InvokedRunFeatureTests.A_reentry_asks_the_decider_afresh_and_never_replays_the_failed_runs_recorded_answer` | `InvokedRunFeatureTests.A_reentry_asks_the_decider_afresh_and_never_replays_the_failed_runs_recorded_answer` | `InvokedRunFeatureTests.A_short_circuit_in_an_invoked_train_is_the_output_its_OnDone_reads` | `InvokedRunOperationsTests.Requeue_of_an_invoked_run_is_refused_with_one_reason_on_both_surfaces` | `InvokedRunIsNeverRetriedTests.The_scheduler_never_retries_or_dead_letters_an_invoked_run` | `InvokeOutcomeDeliveryTests.Leaving_the_invoking_state_cancels_the_run_on_another_host` | `InvokeOutcomeDeliveryTests.A_timed_out_run_and_an_operator_cancel_reach_OnCancelled` | `InvokeOutcomeDeliveryTests.A_timed_out_run_and_an_operator_cancel_reach_OnCancelled` | `InvokedRunFeatureTests.An_invoked_run_writes_its_junction_events_and_progress_as_any_run_does` | `InvokedRunFeatureTests.An_invoked_run_writes_its_junction_events_and_progress_as_any_run_does` | `InvokesStartupRefusalTests.An_output_reaching_a_sensitive_member_is_refused` | n/a: an invoked run starts from the input its state builds from the context and nothing else, so no value of the machine's is in its Memory; an AddServices inside the invoked train runs as in any run | `InvokeOutcomeDeliveryTests.An_outcome_whose_target_invokes_queues_the_next_run_in_the_same_transaction` | `InvokesDeclarationTests.A_tuple_output_is_refused_at_build` | n/a: an invoking state routes its outcome by guarded OnDone edges, not by a routing step, and a Switch inside the invoked train routes as in any run | `InvokedRunFeatureTests.An_invoked_run_sent_to_a_remote_worker_runs_there_and_its_outcome_comes_back` | `InvokedRunIsNeverRetriedTests.The_scheduler_never_retries_or_dead_letters_an_invoked_run` |
