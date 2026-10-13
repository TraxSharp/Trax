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
| Checkpoint | `CheckpointResumeTests.Decisions_after_the_checkpoint_replay_on_resume` | `CheckpointResumeTests.Questions_after_the_point_are_asked_afresh_when_nothing_replays_them` | `CheckpointDeclarationTests.A_checkpoint_after_a_ShortCircuit_on_its_path_is_refused` | `CheckpointResumeTests.A_dead_letter_requeue_resumes_and_requeueExecution_reruns_from_the_top` | `CheckpointResumeTests.A_dead_letter_requeue_resumes_and_requeueExecution_reruns_from_the_top` | `CheckpointResumeTests.A_resumed_run_cancelled_through_its_cancel_flag_on_another_host_is_recorded_cancelled` | `CheckpointResumeTests.A_resumed_run_cancelled_through_its_cancel_flag_on_another_host_is_recorded_cancelled` | `CheckpointResumeTests.A_resumed_run_past_its_job_timeout_is_recorded_cancelled` | `CheckpointResumeTests.A_manifest_retry_after_a_crash_in_Summarize_does_not_rerun_FetchFullTexts` | `CheckpointResumeTests.A_resumed_run_reports_progress_only_for_the_steps_it_runs` | `CheckpointWriteTests.A_sensitive_routing_track_is_never_stored_and_a_checkpoint_inside_it_is_refused` | `CheckpointResumeTests.A_value_handed_to_AddServices_is_handed_again_on_resume` | `CheckpointResumeTests.Node_ids_and_asking_occurrences_after_the_point_equal_the_original_runs` | `CheckpointResumeTests.A_branch_whose_last_step_is_a_reached_checkpoint_runs_nothing_and_gives_the_join_its_state` | `CheckpointResumeTests.Node_ids_and_asking_occurrences_after_the_point_equal_the_original_runs` | `CheckpointResumeTests.A_resume_on_a_remote_worker_restores_the_checkpoint` | `CheckpointResumeTests.A_resumed_retry_waits_out_its_backoff` |

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
kinds' columns. `Invokes` stayed experimental (`TRAXEXP002`) until its row was complete
(`Trax.Docs/adr/0046`).

| Machine feature | Decision replay | Ask afresh | ShortCircuit | Requeue | Dead letters | Cross-host cancel | Dashboard cancel | Job timeout | Junction events | Progress | Sensitive and withheld | AddServices before the fork | Same junction twice | Tuple outputs | Same Switch enum twice | Remote workers | Retry backoff |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Invokes | `InvokedRunFeatureTests.A_reentry_asks_the_decider_afresh_and_never_replays_the_failed_runs_recorded_answer` | `InvokedRunFeatureTests.A_reentry_asks_the_decider_afresh_and_never_replays_the_failed_runs_recorded_answer` | `InvokedRunFeatureTests.A_short_circuit_in_an_invoked_train_is_the_output_its_OnDone_reads` | `InvokedRunOperationsTests.Requeue_of_an_invoked_run_is_refused_with_one_reason_on_both_surfaces` | `InvokedRunIsNeverRetriedTests.The_scheduler_never_retries_or_dead_letters_an_invoked_run` | `InvokeOutcomeDeliveryTests.Leaving_the_invoking_state_cancels_the_run_on_another_host` | `InvokeOutcomeDeliveryTests.A_timed_out_run_and_an_operator_cancel_reach_OnCancelled` | `InvokeOutcomeDeliveryTests.A_timed_out_run_and_an_operator_cancel_reach_OnCancelled` | `InvokedRunFeatureTests.An_invoked_run_writes_its_junction_events_and_progress_as_any_run_does` | `InvokedRunFeatureTests.An_invoked_run_writes_its_junction_events_and_progress_as_any_run_does` | `InvokesStartupRefusalTests.An_output_reaching_a_sensitive_member_is_refused` | n/a: an invoked run starts from the input its state builds from the context and nothing else, so no value of the machine's is in its Memory; an AddServices inside the invoked train runs as in any run | `InvokeOutcomeDeliveryTests.An_outcome_whose_target_invokes_queues_the_next_run_in_the_same_transaction` | `InvokesDeclarationTests.A_tuple_output_is_refused_at_build` | n/a: an invoking state routes its outcome by guarded OnDone edges, not by a routing step, and a Switch inside the invoked train routes as in any run | `InvokedRunFeatureTests.An_invoked_run_sent_to_a_remote_worker_runs_there_and_its_outcome_comes_back` | `InvokedRunIsNeverRetriedTests.The_scheduler_never_retries_or_dead_letters_an_invoked_run` |
| After | n/a: a timer fires no run of its own, and a timer on a user-owned machine may not enter an invoking state, so no decision is recorded for it to replay | n/a: a timer fires no run of its own, so there is no question for it to ask again | n/a: a timer moves a machine from one state to another; it runs no chain that could short-circuit | n/a: a timer queues no work, so there is no run of its own to requeue | `TimerFiringTests.A_fire_that_cannot_be_applied_clears_due_at_and_is_metered` | `TimerFiringTests.A_timer_leaving_an_invoking_state_cancels_its_run_on_another_host` | `TimerFiringTests.An_operator_cancel_and_a_timer_apply_exactly_one` | `TimerFiringTests.The_run_timeout_and_the_state_timer_apply_one` | n/a: a fire runs no junction; it is one conditional write to the snapshot row | n/a: a fire runs no junction, so it reports no progress | `TimerDeclarationTests.A_duration_field_marked_TraxSensitive_is_refused_at_build` | n/a: a fire runs no train, so there is no fork for services to cross | `TimerDeclarationTests.A_second_After_on_one_state_is_refused_at_build` | n/a: a timer carries no output, tuple or otherwise | n/a: a timer has one target and routes by no enum | n/a: a timer leaving an invoking state cancels through the same leaving path as an event, which reaches a run wherever it executes; the fire itself runs on a machine host | n/a: a fire is never retried; one that cannot be applied clears its due time and is metered |
| Per-event rules | n/a: a rule is checked before the transition and starts no run, so it records no decision | n/a: a rule asks the caller's roles, never a decider, so there is nothing to ask afresh | n/a: a rule is checked outside any chain, before the transition runs | n/a: requeue of an invoked run is refused whatever event entered its state, so the rule never meets a requeue | n/a: an invoked run is never dead-lettered, whatever event entered its state | n/a: leaving an invoking state through a gated event cancels the run as any leaving does; the rule is checked before the advance | n/a: an operator cancel runs as Trusted, which per-event rules do not apply to | n/a: a job timeout reaches OnCancelled through the reconciler, a server write no rule applies to | n/a: a rule runs no junction | n/a: a rule runs no junction | n/a: a rule reads only the caller's roles and policies, never the input or the context | n/a: a rule is checked before any run starts, so it never meets a fork | `InvokesStartupRefusalTests.Every_event_entering_the_invoking_state_is_compared` | n/a: a rule reads no output | n/a: a rule routes nothing | n/a: the rule is checked on the host that receives the event, before the run is queued, so where the run executes does not matter | n/a: an invoked run is never retried, whatever event entered its state |
| Typed start | n/a: start creates the draft; a run it queues by entering an invoking initial state replays decisions as any invoked run does | n/a: start asks no decider; the run its initial state queues asks as any invoked run does | n/a: start runs the declared reduction of its input, not a chain | n/a: start queues no work of its own; a run its initial state queues is an invoked run, never requeued | n/a: start queues no work of its own; a run its initial state queues is never dead-lettered | n/a: a draft just started has not left any state, so nothing is cancelled across hosts | n/a: an operator cancel runs as Trusted and never starts a draft | n/a: start completes in its own request; a run its initial state queues times out as any invoked run | n/a: start runs no junction | n/a: start runs no junction | `StartTests.Start_refuses_a_StartInput_reaching_a_server_written_field` | n/a: start runs no train, so there is no fork for services to cross | `InvokesStartupRefusalTests.Start_entering_an_invoking_initial_state_is_compared_too` | n/a: start reads a declared StartInput record, never a tuple | n/a: start routes nothing; it enters the start state | n/a: start runs on the host that receives it; only a run its initial state queues can go to a worker | n/a: start is never retried by the server; a client retry under one request id returns the same instance |

## Machine API features

A machine served as a typed API ([`Trax.Docs/adr/0048`](../../Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md))
crosses the machine features above and the train features a machine reaches through them, so it gets a table of
its own. A **row** is a feature of the machine API, a **column** a feature it has to work with: *Invokes* an
invoking state, *Parallel*, *Checkpoint* and *Decisions* a train an invoking state runs, *System-owned* a machine
no user owns, *Generic opt-in* the six generic mutations behind `ExposeGenericStateMachineMutations(o => o.Policy = ...)`. The cell
rules are the step kinds': a test as `` `ClassName.MethodName` ``, or `n/a: <why it cannot>`.
`InteractionMatrixTests` checks it against the list `MachineApiFeatures`.

| Machine API feature | Invokes | Parallel | Checkpoint | Decisions | System-owned | Generic opt-in |
|---|---|---|---|---|---|---|
| Typed surface | `MachineRequestIdE2ETests.The_entering_event_queues_one_run_under_retries` | n/a: it runs inside the invoked train; the machine sees only the one outcome the run ends in | n/a: it runs inside the invoked train; the machine sees only the one outcome the run ends in | n/a: it runs inside the invoked train; the machine sees only the one outcome the run ends in | `MachineExposureStartupTests.Exposing_a_system_owned_machine_fails_startup_naming_it` | `GenericOptInE2ETests.Without_the_opt_in_the_schema_has_no_stateMachine_field` |
| Per-event rules | `InvokesStartupRefusalTests.An_Operator_event_entering_an_invoking_state_may_invoke_an_Operator_train` | n/a: it runs inside the invoked train; the machine sees only the one outcome the run ends in | n/a: it runs inside the invoked train; the machine sees only the one outcome the run ends in | n/a: it runs inside the invoked train; the machine sees only the one outcome the run ends in | `EventRuleStartupTests.Exposing_a_raw_delegate_or_system_owned_machine_refuses_startup` | `MachineRulesE2ETests.The_generic_opt_in_advance_checks_the_same_event_rule` |
| Revision and feed | `SnapshotChangeOutcomeNotifyTests.An_outcome_applied_by_the_hook_notifies_once` | n/a: however many branches a Parallel runs, the invoked run ends in one outcome, which is one write and one notice | n/a: a resumed run ends in one outcome like any run, which is one write and one notice | n/a: a decision is recorded on the run, never on the snapshot row, so it bumps no revision | `SnapshotChangeNotifyTests.System_start_and_advance_notify_once` | `SnapshotChangeNotifyTests.Save_notifies_once_with_its_row_id` |
| Timers | `TimerFiringTests.A_timer_leaving_an_invoking_state_cancels_its_run_on_another_host` | n/a: it runs inside the invoked train; the machine sees only the one outcome the run ends in | n/a: it runs inside the invoked train; the machine sees only the one outcome the run ends in | n/a: it runs inside the invoked train; the machine sees only the one outcome the run ends in | `TimerDeclarationTests.A_timer_into_an_invoking_state_on_a_system_owned_machine_is_allowed` | `GenericSaveTimerTests.The_generic_save_is_refused_for_a_machine_with_a_timer` |
