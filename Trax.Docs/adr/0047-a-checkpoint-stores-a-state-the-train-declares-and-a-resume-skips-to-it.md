---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, api, dashboard, samples]
areas: [platform, data-model, graphql]
status: accepted
---

# A checkpoint stores a state the train declares, and a resumed run skips to it

A run that failed at its last junction used to repeat every expensive step before it, because a retry or a requeue
starts the chain again from the top. A chain may now declare `Checkpoint<TState>()`: when the run reaches it, the
`TState` in Memory is stored, and a later run of the same input resumes there instead, running only the steps after
it. Trax never stores Memory as a whole. Whether a run can resume at a given step is decided from the declared graph
before anything runs.

## Status

**Accepted.** `Checkpoint<TState>()` and the resume operation shipped behind `[Experimental("TRAXEXP003")]`,
following the convention [0045](./0045-a-parallel-step-runs-fixed-branches-on-copies-of-memory-and-the-join-commits.md)
set, until the `Checkpoint` row of the interaction matrix (`Trax.Core/docs/interaction-matrix.md`) was complete; it
is complete and the attribute is gone.

The plan put this decision in Trax.Effect, beside effect/0019. It is central because the scheduler's retries, the
mediator's enqueue, both operator surfaces and the samples each have a part of it that they could break without
realising.

## Why this is written down

Because a chain is ordinary code, not a list of steps: `Junctions()` runs each step as it is called, and the only
way to skip one is the guard every step already has. A resume therefore cannot jump; it runs `Junctions()` again and
skips in place. Everything below follows from that, or from closing one way a resume could quietly be wrong: a write
it skips that never committed, a stored state that no longer matches its type, a step that needs a value nothing
restored, and two resumes of one run.

### What is stored

**The train declares the state; Trax never dumps Memory.** Memory holds services, data contexts and objects changed
in place, none of which survive being written down and read back. `TState` is a type the author chooses, put in
Memory by an earlier step, and it is the only value a checkpoint stores, with the track each routing step before it
took. Services are resolved from the container again on resume, and `Seed` and `AddServices` values are given again,
because `Junctions()` runs again.

**`TState` must round-trip, checked at startup.** A type that is not sealed, has a member typed `object` or an
interface, or has a field the serializer skips, is refused, naming the member. Otherwise the stored JSON reads back
as a different value, and a decision asked after the resume hashes a different state (effect/0020), so its recorded
answer silently stops replaying.

**A sensitive state is refused.** A `TState` reaching a `[TraxSensitive]` member is refused at startup, and checked
again, cached, when the checkpoint is written, because the startup answer covers only the assemblies loaded then.
Encrypting such a state is a decision of its own. A track whose routing type is sensitive is withheld from the
stored tracks, as it is withheld from junction events, so a checkpoint inside such a track is refused at startup: a
resume could not find its way back into it.

**A checkpoint row is guarded against deploys.** Each row carries the chain's hash (`ChainGraph.Hash`, over the
declared graph) and a fingerprint of `TState`'s serializer contract, and a resume refuses, with the reason, when
either differs from the running code. A full rerun stays possible. The plan put the chain hash on the run's metadata
at start; it is on the checkpoint row instead, since that is the only place it is compared, and putting it on every
run would add a write to runs that never checkpoint. The hash covers names, types and tracks, not a gate's
threshold or a seed's value: a resume takes the routes the stored run took, even if the code would now route
differently.

**The size cap is requeue's.** A checkpoint is stored as canonical JSON under the cap a requeue's stored input uses
(4 times `MaxInputJsonBytes`, 1 MiB by default). Over it, the step fails, classified permanent, naming the size and
the cap. A placeholder is never stored: the parameter provider's truncation is the wrong model here, because a
truncated checkpoint is a resume that cannot happen.

**A new table in the core provider set** ([0009](./0009-feature-tables-ship-in-the-core-provider-set.md),
[0036](./0036-a-feature-table-ships-with-its-model-in-effect.md)): `trax.checkpoint`, one row per run and node, for
Postgres, Sqlite and InMemory, deleted with its run by a cascade. It holds no run data on any surface: operators see
that a run has a checkpoint and at which node, never what it holds. effect/0019 stays true: `junction_run` still
carries no run data.

### The commit boundary

**A checkpoint commits nothing of the run's; it refuses to be taken over uncommitted work.** The plan made the
checkpoint the one mid-run commit of the run's tracked writes. The code says otherwise: the only thing the run tracks
is its own metadata row, a failed run saves it anyway, and a consumer's writes go through their own scoped data
context and commit when their junction commits them. So the row is written through a data context of its own, as
junction progress is (effect/0021), and effect/0021 keeps having no exception. What a resume must not skip is work
that never committed: so writing a checkpoint fails, permanent, when the step's scoped Trax data context has
uncommitted changes or an open transaction. A checkpoint placed between `BeginTransaction` and `CommitTransaction`
is the case it refuses.

**Steps after the checkpoint run at least once.** A run that crashed after a checkpoint resumes from it, so the steps
between the checkpoint and the failure run again. They must be idempotent, as every retried step must, and anything
irreversible takes its own claim.

### How a run resumes

**A resumed run skips in place.** It runs `Junctions()` again with a resume point. Every step before the point
returns without running: no junction is called and no decider is asked, but each step still advances the node
numbering and the asking counters exactly as it did, so every id after the point, and every recorded answer's
occurrence, is the one the original run had. A routing step before the point takes the track the checkpoint stored.
At the checkpoint, its `TState` and tracks are put in Memory, beside the run's input and the services. A
`ShortCircuit` before a checkpoint on the same path is refused at startup: its value lives outside Memory and a
resume would lose it.

**Whether a run can resume at a step is decided before it runs.** A forward walk over the declared graph, per path,
from the resume point: every step's inputs must come from what the checkpoint restores, the train's input, or a step
on that path after the point. The decisions themselves (`ChoiceDecision` and the rest) are not stored, so a step
that reads one from before the checkpoint makes the check refuse. A refusal is typed and names the step and the type,
for example "`Summarize` needs `Findings`; no checkpoint before it holds one". An operator may resume at any step the
check allows, not only at a checkpoint; the steps between the checkpoint and that step are skipped too.

**Decisions after the resume point replay.** A resumed run replays the decisions of the run it resumes, as a requeue
does ([0041](./0041-a-requeued-run-replays-the-decisions-of-the-run-it-repeats.md)), bounded by effect/0020's state
hash and age.

**Inside a `Parallel`, each branch resumes from its own checkpoint.** A checkpoint in a branch is stored under the
branch's path. A resume into a failed `Parallel` reruns each branch from its own latest checkpoint, and from the
start when it has none; a branch whose last step is a checkpoint it reached runs nothing, and what it gives the join
is what its checkpoint restores. A branch's other outputs are not kept automatically: an author who needs them at
the join puts them in the branch's checkpoint state, and the check refuses a join that would miss one.

### Which runs resume

**A resumed run links back to the run it resumes.** `resume_from` (the run) and `resume_at` (the node, or null for
"after its latest checkpoint") go on the work queue entry and are copied to the run, as `replay_decisions_of` is. A
resumed run writes only the checkpoints after its resume point, so "the latest checkpoint" is followed back through
`resume_from`: a run's checkpoints are its own rows and the one it resumed from. A second failure after a resume
therefore resumes from the same checkpoint, not from the top. A unique index allows one queued resume per run, which
also settles an operator's resume racing a scheduled retry: one is queued and the other is refused.

**Which paths resume:**
- **A manifest's retry** resumes after the failed run's latest checkpoint, whenever it has one; otherwise it reruns
  the chain as today. Declaring a checkpoint is the opt-in. The lookup is the retry's (the latest failed run of the
  manifest, same input, no subject key), and it does not depend on the manifest replaying decisions.
- **A dead letter's requeue** resumes the same way.
- **`requeueExecution`** keeps its meaning: run it again from the top.
- **`resumeExecution(id, from)`** is the operator's resume: at a named step, or after the latest checkpoint when
  `from` is omitted. It is the dashboard's "Resume from here" on the run graph's nodes, and the two share one
  operation ([0022](./0022-the-dashboard-and-the-api-share-one-operation-per-action.md)).
- **A machine re-entering an invoking state** starts a new run from the top. The state is the machine's checkpoint
  ([0046](./0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md)); a stage that needs
  finer resume is two states. Its input is built again from the context and may differ (the topic map mints a fresh
  id on purpose).
- **Ad-hoc runs** have no automatic retry; an operator may resume them.

**An operator's resume is a requeue in every check but where it starts.** It refuses a run that is not Failed or
Cancelled, a run that already has a queued resume, a run a machine invoked (0046's reason), and an input the
requeue's input check refuses (missing, a placeholder, or masked). On GraphQL it needs the operations gate and the
train's own `[TraxAuthorize]`, through the mediator, as a requeue does; resuming does strictly less than requeueing
the same input, so it needs no stronger role. The dashboard calls it in its trusted scope and records no actor, as
it records none for a requeue; adding one is a change to every dashboard action, not to this one.

**Cleanup keeps a checkpoint a resume needs.** Metadata cleanup keeps a run while a queued entry or a run that stays
names it in `resume_from`, as it keeps a run another will replay, and deletes a resumed run with its source when
both have expired. A run that completes deletes its own checkpoint rows, since nothing may resume it.

## Considered options

**Store Memory.** Rejected: it holds services, contexts and objects changed in place.

**Make the checkpoint commit the run's tracked writes.** Rejected once checked against the code: there is nothing of
the consumer's in them to commit, and saving through the effect runner mid-run saves every provider, the cost
effect/0021 removed. Refusing a checkpoint over uncommitted work is the rule that protects a resume.

**Encrypt sensitive checkpoint state.** Not now: refusing it fails closed, and encryption can be added without
changing what is stored for every other state.

**Make `requeueExecution` resume.** Rejected: "run it again" and "carry on" are different requests, and an operator
who wants a clean run must still be able to ask for one.

**Resume an invoked train when its machine re-enters the state.** Rejected: the input is built again and may differ,
and the state boundary already is the stage's checkpoint.

**Checkpoint a branch's outputs automatically at the join.** Rejected: it is storing Memory by another name. The
branch's author declares what to keep.

## Tests

Written first, red, before the code they cover. Each names the class and method it will have.

In `Trax.Core/tests/Trax.Core.Tests.Unit`:
- `CheckpointDeclarationTests.A_state_that_is_not_sealed_is_refused_naming_the_type`
- `CheckpointDeclarationTests.A_state_with_an_object_or_interface_member_is_refused_naming_the_member`
- `CheckpointDeclarationTests.A_state_with_a_field_the_serializer_skips_is_refused_naming_the_field`
- `CheckpointDeclarationTests.A_state_no_earlier_step_puts_in_Memory_is_refused`
- `CheckpointDeclarationTests.A_checkpoint_after_a_ShortCircuit_on_its_path_is_refused`
- `CheckpointDeclarationTests.A_checkpoint_exports_as_its_own_node_kind_with_its_state_type`
- `CheckpointResumeTests.Steps_before_the_resume_point_run_nothing_and_ask_no_decider`
- `CheckpointResumeTests.Node_ids_and_asking_occurrences_after_the_point_equal_the_original_runs`
- `CheckpointResumeTests.A_routing_step_before_the_point_takes_the_stored_track`
- `CheckpointResumeTests.A_branch_whose_last_step_is_a_reached_checkpoint_runs_nothing_and_gives_the_join_its_state`
- `CheckpointResumeTests.Resuming_a_failed_Parallel_reruns_only_the_failed_branch_from_its_checkpoint`
- `ResumeCheckTests.A_type_produced_after_the_step_that_needs_it_is_refused`
- `ResumeCheckTests.A_step_reading_a_decision_from_before_the_checkpoint_is_refused_naming_it`
- `ResumeCheckTests.A_join_missing_a_skipped_branchs_output_is_refused`
- `ResumeCheckTests.A_chain_hash_or_state_fingerprint_mismatch_is_refused_with_its_own_reason`
- `ResumeCheckPropertyTests.The_check_agrees_with_running_from_the_point_over_generated_chains`: when it allows a
  resume, the resumed run equals a full run; when it refuses, running from the point hits a missing input.
- `CheckpointStatePropertyTests.StateDigest_of_a_generated_state_survives_a_round_trip`

In `Trax.Effect/tests/Trax.Effect.Tests.Integration`, on Postgres and Sqlite, and InMemory where it has the table:
- `CheckpointWriteTests.A_checkpoint_stores_its_state_tracks_hash_and_fingerprint_once_per_node`
- `CheckpointWriteTests.A_checkpoint_over_the_cap_fails_the_step_as_permanent_and_stores_nothing`
- `CheckpointWriteTests.A_checkpoint_over_uncommitted_changes_or_an_open_transaction_fails_and_stores_nothing`
- `CheckpointWriteTests.A_checkpoint_saves_no_other_effect_provider`
- `CheckpointWriteTests.A_sensitive_state_is_refused_at_startup_and_at_write_after_an_assembly_loads`
- `CheckpointWriteTests.A_sensitive_routing_track_is_never_stored_and_a_checkpoint_inside_it_is_refused`
- `CheckpointWriteTests.A_completed_run_deletes_its_checkpoints`
- `CheckpointWriteTests.Two_branches_writing_checkpoints_at_once_store_both`

In `Trax.Scheduler/tests/Trax.Scheduler.Tests.Integration`, on Postgres and Sqlite:
- `CheckpointResumeTests.A_manifest_retry_after_a_crash_in_Summarize_does_not_rerun_FetchFullTexts`, proved by its
  junction events
- `CheckpointResumeTests.A_write_before_the_checkpoint_exists_once_after_a_crash_and_a_resume`
- `CheckpointResumeTests.A_second_failure_after_a_resume_resumes_from_the_same_checkpoint`
- `CheckpointResumeTests.Decisions_after_the_checkpoint_replay_on_resume`
- `CheckpointResumeTests.A_dead_letter_requeue_resumes_and_requeueExecution_reruns_from_the_top`
- `CheckpointResumeTests.A_run_without_a_checkpoint_retries_from_the_top`
- `CheckpointResumeTests.An_old_checkpoint_after_a_state_or_chain_change_is_refused_and_the_retry_reruns_from_the_top`
- `CheckpointResumeTests.An_operator_resume_racing_a_manifest_retry_queues_exactly_one`
- `CheckpointResumeTests.A_resume_on_a_remote_worker_restores_the_checkpoint`
- `CheckpointCleanupTests.Cleanup_keeps_a_source_run_while_a_queued_resume_names_it`, on InMemory as well
- `CheckpointCleanupTests.Cleanup_deletes_a_runs_checkpoints_with_it`, on InMemory as well

In `Trax.Api/tests`, on GraphQL and the dashboard alike:
- `ResumeExecutionOperationsTests.A_resume_gives_the_same_result_on_both_surfaces`
- `ResumeExecutionOperationsTests.A_point_no_checkpoint_covers_is_refused_with_one_reason_on_both_surfaces`
- `ResumeExecutionOperationsTests.A_running_pending_or_completed_run_is_refused`
- `ResumeExecutionOperationsTests.A_second_resume_while_one_is_queued_is_refused`
- `ResumeExecutionOperationsTests.A_resume_of_an_invoked_run_is_refused_with_its_reason`
- `ResumeExecutionOperationsTests.A_placeholder_or_masked_input_is_refused`
- `ResumeExecutionOperationsTests.A_caller_past_the_gate_but_not_the_trains_authorize_is_refused`
- `ResumeExecutionOperationsTests.No_operator_surface_returns_a_checkpoints_state_or_a_withheld_track`
- `RunGraphViewTests.A_failed_runs_nodes_offer_Resume_from_here_only_where_the_check_allows`

In `Trax.Samples/tests/Trax.Samples.Recovery.E2E`:
- `ResearchResumeTests.A_crash_in_Summarize_resumes_from_CheckedFindings_without_fetching_again`, on GraphQL and the
  dashboard, with `TraxInvariants` clean after it

## Exemplars

**Enforced elsewhere:** the tests listed under *Tests*, in Trax.Core, Trax.Effect, Trax.Scheduler, Trax.Api and
Trax.Samples, each written red before the code it covers; `InteractionMatrixTests` in `Trax.Core.Tests.Meta`, which
requires a `Checkpoint` row naming a test for every existing feature once `ChainStepKind.Checkpoint` exists; and
`TraxInvariants` in Trax.Effect.Data.Testing, which reports a checkpoint row of a completed run and a `resume_from`
naming a run that does not exist.

Not covered: nothing can check that the steps after a checkpoint are idempotent, or that a consumer's data context
other than Trax's own has committed its work when a checkpoint is written. Both are conventions the docs state.

## Changelog

- **2026-10-08**: `[Experimental("TRAXEXP003")]` lifted: the `Checkpoint` row of the interaction matrix is
  complete, every cell a test.

- **2026-10-08**: Accepted after review. The user confirmed each contested point: a checkpoint refuses
  uncommitted work rather than committing the run's writes; manifest retries and dead-letter requeues resume
  automatically while `requeueExecution` reruns from the top; branches resume from checkpoints their author
  declares; an over-cap state fails the step; a machine's re-entry starts fresh; an operator may resume at any step
  the check allows; decision records are not stored; both surfaces ship, the dashboard recording no actor.
- **2026-10-08**: Recorded.
