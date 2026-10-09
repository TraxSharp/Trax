---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, api, dashboard, cli, samples]
areas: [platform, data-model, graphql]
status: accepted
---

# A machine state invokes a train, and only that entry of the state receives its outcome

Long-running work with several stages (fetch, normalise, resolve, embed) had nowhere durable to record where each unit
of work was, so it became one large train or hand-written glue. A state machine now runs trains: a state declares
`Invokes<TTrain, TInput, TOutput>(ctx => input)` (C# cannot infer the output type from the train's interface), entering it queues one run in the same transaction as the advance, and the run's
outcome comes back as a trigger that only the entry which queued it can apply. Leaving the state cancels the run. A
state is therefore a durable checkpoint between stages: a failed stage is retried by entering its state again, never by
the scheduler.

## Status

**Accepted.** `Invokes` and `IMachineInstances.Start` shipped behind `[Experimental("TRAXEXP002")]`, following the
convention [0045](./0045-a-parallel-step-runs-fixed-branches-on-copies-of-memory-and-the-join-commits.md) set,
until the `Invokes` row of the interaction matrix's machine-features table (`Trax.Core/docs/interaction-matrix.md`)
was complete; it is complete and the attribute is gone. A machine feature is not a chain step kind, so it gets a
table of its own with the same columns.

## Why this is written down

Because a machine and a train were separate engines that never referred to each other, and joining them opens four
holes at once: a run orphaned by a crash, a stale or forged completion moving a machine, a caller starting work they
may not start, and a client twin that disagrees with the server. Each rule below closes one of them. The model is
XState's `invoke` and the Elm architecture: the transition stays pure and returns a command, the runtime runs the
command, and its single `Either` is fed back in as the next message.

### Shape

**A run belongs to a state, not to a transition.** A run fired from a transition and forgotten would outlive the state
that wanted it and deliver late results into whatever state the machine had moved on to. Tied to the state, leaving
the state ends it, and a late completion has nowhere to land.

**Every invoking state says where each outcome goes.** One or more `OnDone(target)` edges, each with an optional
declarative guard on the train's output and a declarative `Reduction` into the context; one `OnFailed(target)`; and
an `OnCancelled(target)` that is required, so a cancel always has a declared edge (and an operator's cancel is never
"its failure state", which is undefined once a machine has several).

**There is no `OnDecisionUnsure`.** A run ends in one `Either`, so "unsure" is not an outcome of its own. A train that
can be unsure says so in its output, and a guarded `OnDone` routes it (`out is Unsure` to `NeedsReview`).

**The machine package does not depend on the mediator.** Effect.StateMachine declares an `IInvokedTrainLauncher` port
and Mediator implements it. A host that declares `Invokes` without registering Mediator fails at startup. A new
package above Scheduler was the other place it could live, and would have been one more package for a single port.

### Ownership and identity

**Some machines belong to the system.** Large fan-out (one instance per partition of a source) needs instances no user
owns. `IMachineInstances.Start<TMachine>(key, context)` creates them, from trains or at startup; GraphQL cannot. Owner
is an `owner_kind` column on `snapshot_draft` (`user` or `system`), with `user_key` null on system rows, and every
user query of the snapshot store filters `owner_kind = user`. A reserved owner key string was the first answer and was
dropped: each host maps principals to keys in its own `ISnapshotPrincipal`, so any host's mapping could produce the
reserved string. For the same reason a null, empty or whitespace user key is no principal at all: such a caller is
`unauthenticated`, rather than one owner shared by every caller the mapping could not name.

**A system instance's id is derived from its key.** UUIDv5 of the machine's fixed namespace UUID and a canonical,
injective encoding of the key, so `("a|b", "c")` and `("a", "b|c")` differ, and `Start` twice finds the same instance
and queues nothing new. Because a user could hold the same id under their own key, every lookup by id names the owner
kind as well.

### Correlation and delivery

**An outcome is correlated by a server-only token.** Entering an invoking state sets `invoke_token` (a column with a
unique index) to the queued `work_queue` row's `ExternalId`; leaving the state clears it. It is never in the context,
which a client can rewrite. An outcome is applied by `UPDATE … WHERE invoke_token = @token`, so a completion whose token
was cleared or replaced is a typed `no-transition`, a duplicate delivery applies once, and one token per entry means
at most one live run per entry.

**A sweep guarantees delivery; the lifecycle hook is the fast path.** Lifecycle hooks run in the process that ran the
train and swallow exceptions, and a run failed by the reaper publishes nothing from a train at all, so a hook alone
can lose an outcome. A reconciler, a hosted service in Effect.StateMachine.Persistence started on every host that
registers machines (it needs their definitions), sweeps snapshots whose token names a finished run and applies the
outcome. Several hosts sweeping at once is harmless, because the conditional update applies once. A cancelled run maps
to `OnCancelled`; a reaped one reaches `OnFailed` through the sweep, and the reaper publishes its own terminal event
for every other subscriber.

**An outcome too large to store fails the state.** An outcome that would push the snapshot past its 64 KiB cap goes to
`OnFailed` with a typed reason. It is never dropped.

**A run that can no longer be found fails the state.** A token whose run has neither a work queue entry nor a
metadata row was deleted before its outcome was delivered, and can never end, so it goes to `OnFailed` with a typed
reason. Metadata retention keeps an invoked run while an instance holds its token, so this is a last resort.

### Queueing

**The advance and the enqueue commit together.** The `work_queue` row is written through the caller's own `DbContext`,
in the transaction that advances the snapshot, so a crash leaves neither half: no machine waiting on a run that was
never queued, and no run whose machine never moved. The point between the two writes is the fault the design is built
around. It follows that an invoked train may not use deferred promotion or an `OnQueue` hook (both commit separately),
that the advance's own progress saves must not commit or abort that transaction part-way
(effect/0021), and that
`Invokes` is refused on the InMemory provider, which has no transactions. On Postgres a `pg_notify` sent in the same
transaction wakes the dispatchers on every host when it commits, and not at all if it rolls back; API hosts, where
machines advance, are usually not the scheduler hosts, so a same-process wake would rarely help. Sqlite gets a
same-process wake.

**Queueing is exactly once; running is at least once.** The outbox makes the enqueue exactly once. The train itself can
run more than once, so its junctions are idempotent, and an irreversible step inside it takes its own claim. An invoked
train does not count against a machine's single irreversible effect.

**The scheduler never retries an invoked run, and an operator cannot requeue one.** A failure goes to `OnFailed`, and
the machine retries by entering the invoking state again, which mints a new run and a new token; the old run's late
completion is then a `no-transition`. The state is the checkpoint. Carrying the token through manifest retries, dead
letters and requeue was the alternative, and would have given one entry several runs over time, each able to deliver.
Requeue is refused with the same reason on GraphQL and the dashboard
([0022](./0022-the-dashboard-and-the-api-share-one-operation-per-action.md)).

### Cancellation

**Leaving the state cancels the run, from any host.** A machine advances on an API host while its train runs on a
scheduler host, so the cancel has to cross hosts. It does that today only through the database cancel flag, which is
read by `CancellationCheckProvider`, a junction effect that only `EffectJunction` runs and that ships in the opt-in
`Trax.Effect.JunctionProvider.Progress` package. Core's plain `Junction` checks only the in-process token. Cross-host
cancel therefore keeps requiring `EffectJunction`. Moving the flag check into Core's train loop was the other option;
it was not chosen here, to keep this change inside Effect, and remains open as a decision of its own.

So `Invokes<TTrain>` refuses at startup a train it cannot cancel: one containing a plain `Junction`; an `IChain<I>`
whose interface does not derive from `IEffectJunction`; a train that is not a `ServiceTrain` (its `Run` can be
overridden); any refusal the chain recorder reports; a `Parallel` branch containing any of these; and a host without
`CancellationCheckProvider` registered. `EffectJunction.RailwayJunction` is sealed, so a subclass cannot skip the
check. Two limits remain and are stated rather than fixed: a slow decider in `Decide` or `Gate` runs outside any
junction, so no cancel check happens while it runs; and a train started from inside a junction is not cancelled with
its parent.

**A machine leaves an invoking state only through a declared transition**: a user's own event (such as "cancel
build"), `OnCancelled`, or an operator's cancel. Never through autosave (below).

**Draft expiry never strands a run.** System rows are exempt from the draft time-to-live, and deleting any draft that
holds a live token cancels its run first.

### Security

**An outcome cannot be forged, on user-owned machines too.** A user-owned machine that invokes a train (a wizard whose
`Building` state runs the build) is exactly where a client would like to skip the work. Invoking states and every
`OnDone`, `OnFailed` and `OnCancelled` target join the reserved set: autosave cannot enter or leave an invoking state
or enter an outcome target, `advanceSnapshot` refuses the outcome triggers as it refuses `effect-bound`, and the
startup refusal of other edges into effect targets extends to outcome targets. One exception: an outcome
target that itself invokes a train may be entered by an ordinary edge, because entering it only queues a new run and
forges no result, and that edge is how a chained stage is retried.

**The train is authorized against the user who entered the state, at entry.** At startup a user-owned machine is
refused a train whose `[TraxAuthorize]` is stricter than the machine's own mutations (entering the state would be a
way around it), and any `[TraxBroadcast]` train, whose subscribers see every run's output. A system-owned machine's
train runs under Trax's trusted execution scope, as a scheduled manifest run does, and may invoke only a train a
scheduled manifest could run: no user-only requirements, checked at startup. Only a system-owned machine chains runs
through outcomes: a run an outcome queues has no user present to authorize it, so a user-owned machine whose
`OnDone`, `OnFailed` or `OnCancelled` enters an invoking state is refused at startup, and chains its stages through
an event the user sends instead.

**A user owner has at most 10 live invoked runs**, counted across every machine; entering an invoking state past the
cap is refused with a typed reason. A run is live from the entry that queues it until it ends, so a dispatched run
whose state was left, which is only flagged for cancel and runs on to its next junction, still counts. A machine may
set its own limit, which is compared with the same count: entering its invoking states is refused once the user holds
that many live runs in all machines together. A lower limit tightens entry into that machine only, and the most a
user can hold is the largest limit among the host's machines. The count is taken under a lock on the user, so
concurrent entries in any machines, on any hosts, cannot pass it together. System owners are not capped here: the
dispatcher's `MaxActiveJobs` bounds them.

**A sensitive output is refused at startup.** The output is reduced into the context, stored as plain `jsonb` and
returned by `loadSnapshot`, so an output type that reaches a `[TraxSensitive]` member is refused. `OnDone` is never
built on the run's recorded output, which is redacted, size-limited and can be null.

**A snapshot holds pointers, not data**: fingerprints and dataset URIs, never rows. Trax's database writes grow with
the number of steps, not the number of rows.

### Operators and the TypeScript twin

**Operators see instances read-only, without the context.** Under the operations gate they see system and user
instances: state, timestamps, owner kind and the runs each invoked. Not the context: it is an untyped `JsonObject`, so
nothing can mask its sensitive parts. They can cancel a system-owned instance, which cancels its live run (or marks a
still-queued row cancelled, racing dispatch cleanly) and moves it through `OnCancelled`. No surface creates or advances
a system instance.

**There is one engine, and the twin sees outcomes as events.** Outcomes are a new IR trigger kind, scoped per invoke
edge, whose input schema comes from the train's output type. Because `OnDone`'s reduction is declarative, the twin
applies it exactly as the server does; a delegate guard or reducer cannot be exported, so a machine mixing one with
declarative edges is refused export rather than exported as an unconditional edge. The twin never runs a train. The
differential corpus claims parity only for the pure half: the twin holds no token, so it cannot tell a stale completion
from a live one.

## Considered options

**A run fired from a transition.** Rejected: it orphans runs and lets late results through (above).

**A reserved owner key for system instances.** Rejected for `owner_kind`: any host's principal mapping could produce
the key.

**Delivery by lifecycle hook only.** Rejected: hooks are in-process, swallow exceptions, and miss reaped runs.

**Scheduler retries carrying the token.** Rejected: retry by re-entry keeps one run per entry, and makes the state the
checkpoint.

**An `OnDecisionUnsure` outcome.** Rejected: a run has one `Either`; a guarded `OnDone` routes "unsure".

**The cancel check in Core's train loop.** Not chosen: requiring `EffectJunction` keeps this change inside Effect.

## Tests

Written first, red, before the code they cover. Each names the class and method it will have.

In `Trax.Effect/tests/Trax.Effect.StateMachine.Tests`:
- `InvokesDeclarationTests.An_invoking_state_without_OnCancelled_is_refused_at_build`
- `InvokesDeclarationTests.A_guarded_OnDone_routes_an_unsure_output_to_its_own_target`
- `InvokesDeclarationTests.Outcomes_export_as_their_own_trigger_kind_per_invoke_edge_with_the_output_schema`
- `IrExporterTests.A_machine_mixing_a_delegate_guard_with_a_declarative_one_is_refused_export`
- `MachineInstanceIdTests.Keys_a_b_c_split_differently_give_different_ids`
- `DifferentialCorpusReplayTests.Outcome_triggers_apply_the_same_reduction_in_the_twin`

In `Trax.Effect/tests/Trax.Effect.StateMachine.Persistence.Integration`, on Postgres and Sqlite:
- `InvokesStartupRefusalTests`, one test per refusal, each naming the offending junction, train or member:
  `A_plain_Junction_is_refused`, `An_IChain_over_a_non_effect_interface_is_refused`,
  `A_train_that_is_not_a_ServiceTrain_is_refused`, `A_recorder_refusal_is_refused`,
  `A_plain_Junction_inside_a_Parallel_branch_is_refused`, `A_host_without_CancellationCheckProvider_is_refused`,
  `A_host_without_Mediator_is_refused`, `Deferred_promotion_or_an_OnQueue_hook_is_refused`,
  `Invokes_on_InMemory_is_refused`, `A_user_owned_machine_invoking_a_stricter_authorized_train_is_refused`,
  `A_user_owned_machine_invoking_a_broadcast_train_is_refused`,
  `A_user_owned_machine_whose_outcome_enters_an_invoking_state_is_refused`,
  `A_system_owned_machine_invoking_a_train_with_user_only_requirements_is_refused`,
  `An_output_reaching_a_sensitive_member_is_refused`, `RailwayJunction_cannot_be_overridden`
- `InvokedTrainOutboxTests.Entering_an_invoking_state_queues_one_run_and_sets_the_token_to_its_ExternalId`
- `InvokedTrainOutboxTests.A_fault_between_the_advance_and_the_enqueue_commits_neither`
- `InvokedTrainOutboxTests.Progress_saves_cannot_commit_or_abort_the_outbox_part_way`
- `InvokeOutcomeDeliveryTests.A_crash_after_the_run_finishes_and_before_the_hook_is_applied_once_by_the_reconciler`
- `InvokeOutcomeDeliveryTests.Two_hosts_delivering_one_completion_apply_it_once`
- `InvokeOutcomeDeliveryTests.A_completion_after_the_state_was_left_is_a_no_transition`
- `InvokeOutcomeDeliveryTests.A_run_failed_by_the_reaper_reaches_OnFailed`
- `InvokeOutcomeDeliveryTests.A_timed_out_run_and_an_operator_cancel_reach_OnCancelled`
- `InvokeOutcomeDeliveryTests.Reentering_after_OnFailed_queues_a_new_run_and_the_old_completion_is_a_no_transition`
- `InvokeOutcomeDeliveryTests.An_outcome_past_64_KiB_goes_to_OnFailed_with_its_reason`
- `InvokeOutcomeDeliveryTests.A_run_whose_records_were_deleted_reaches_OnFailed`
- `InvokeOutcomeDeliveryTests.Metadata_cleanup_keeps_an_invoked_run_while_its_token_is_live`
- `InvokeOutcomeDeliveryTests.Leaving_the_invoking_state_cancels_the_run_on_another_host`
- `SystemOwnedInstanceTests.Start_twice_with_one_key_returns_one_instance_and_queues_nothing_new`
- `SystemOwnedInstanceTests.A_system_instance_refuses_advance_and_save_from_any_user`
- `SystemOwnedInstanceTests.A_system_instance_never_appears_in_a_users_load`
- `SystemOwnedInstanceTests.A_user_holding_the_same_id_does_not_reach_the_system_row`
- `InvokesReservedStateTests.Autosave_into_an_invoking_state_is_refused`
- `InvokesReservedStateTests.Autosave_out_of_an_invoking_state_is_refused`
- `InvokesReservedStateTests.Autosave_into_an_outcome_target_is_refused`
- `InvokesDeclarationTests.An_outcome_sent_to_the_initial_state_is_refused_at_build_naming_why`
- `InvokesReservedStateTests.Advance_with_an_outcome_trigger_is_refused`
- `InvokesReservedStateTests.A_rewritten_context_does_not_change_correlation`
- `SnapshotOwnerKeyTests.An_empty_or_whitespace_key_is_unauthenticated_for_every_mutation_and_writes_nothing`
- `InvokesDraftExpiryTests.Draft_expiry_leaves_system_rows_alone`
- `InvokesDraftExpiryTests.Deleting_a_draft_with_a_live_token_cancels_its_run_first`
- `InvokesAuthorizationTests.The_eleventh_live_run_for_one_user_is_refused_with_its_reason`
- `InvokesAuthorizationTests.A_machines_limit_counts_the_users_live_runs_in_every_machine`
- `InvokesAuthorizationTests.Leaving_and_entering_again_cannot_pile_up_runs_that_are_still_executing`
- `InvokesAuthorizationTests.Concurrent_entries_by_one_user_cannot_pass_the_limit_together`
- `InvokesAuthorizationTests.System_owners_are_not_capped`
- `InvokesAuthorizationTests.A_user_owned_machines_train_runs_as_the_entering_user`
- `InvokesAuthorizationTests.A_system_owned_machines_train_runs_under_the_trusted_scope`
- `InvokesModelTests.Generated_triggers_autosaves_and_completions_keep_every_snapshot_valid_and_orphan_no_run`: a
  CsCheck model-based property over late, duplicated and out-of-order completions, completions after a cancel, from
  two hosts and after a reap; each entry into a state has at most one live run.

In `Trax.Scheduler/tests`, on Postgres:
- `DispatcherWakeTests.A_run_queued_on_another_host_starts_within_a_second_despite_a_30_second_poll`
- `DispatcherWakeTests.A_rolled_back_enqueue_sends_no_notification`
- `OutOfTrainLifecycleEventTests.A_reaped_InProgress_run_and_a_reaped_Pending_run_each_publish_Failed_once`

In `Trax.Api/tests`, on GraphQL and the dashboard alike:
- `InvokedRunOperationsTests.Requeue_of_an_invoked_run_is_refused_with_one_reason_on_both_surfaces`
- `InvokedRunOperationsTests.An_operator_cancel_racing_dispatch_of_a_queued_run_ends_exactly_one_way`
- `InvokedRunOperationsTests.No_operator_surface_returns_a_snapshots_context`

## Exemplars

**Enforced elsewhere:** the tests listed under *Tests*, in Trax.Effect's state machine test projects, Trax.Scheduler's
and Trax.Api's tests, each written red before the code it covers; `InteractionMatrixTests` in `Trax.Core.Tests.Meta`,
which requires the `Invokes` row of the interaction matrix's machine-features table to name a test for every existing
feature.

Not covered: nothing can check that an invoked train's junctions are idempotent, or that a snapshot holds pointers
rather than data. Both are conventions the docs state, and the at-least-once guarantee holds only while junctions
follow the first.

## Changelog

- **2026-10-08**: `Build` refuses an outcome sent to the machine's start state, naming why. The start state joined
  the reserved states, so autosave refused to create any draft (`state-reserved`) and the machine was unusable, and
  any ordinary edge back to the start was refused as an edge into an outcome target, a message that hid the cause.
  A start state that itself invokes a train is reserved already and is not affected.
- **2026-10-08**: The live-run cap is per user across every machine, and counts runs, not tokens. It counted the
  user's rows holding an invoke token in one machine, so N machines gave N caps, and leaving a state cleared its
  token at once while a dispatched run only flagged for cancel kept executing: entering, waiting for dispatch and
  leaving again piled up executing runs bounded only by `MaxActiveJobs`. It now counts the user's runs that are queued,
  or dispatched and not ended, through the work queue's and the run's link to the instance, under a lock on the user;
  `InvokedRunLimit(n)` is compared with that one count.
- **2026-10-08**: `[Experimental("TRAXEXP002")]` lifted: the `Invokes` row of the interaction matrix is complete.

- **2026-10-08**: `InvokesModelTests` lives in `Trax.Scheduler/tests/Trax.Scheduler.Tests.Integration`, on Postgres
  and SQLite, beside the delivery tests: its operations need the dispatcher, the job runner, the reaper and the
  reconciler, and it drives them through the same cluster of hosts, against a model of each instance's state, token
  and runs. Besides the completions above it generates user leaves racing a delivery, a dispatch or the run itself,
  two hosts starting one system instance, operator cancels, and draft expiry.
- **2026-10-08**: The interaction matrix's machine-features table has its `Invokes` row, which
  `InteractionMatrixTests` checks as it checks the step kinds' rows. Filling it found that a tuple output read as
  nothing: a tuple's elements are fields, which neither the stored output nor the exported schema carries, so every
  guard read false and every reduction wrote null. `Build` now refuses a tuple output, naming the state.
- **2026-10-08**: `IMachineInstances.Advance<TMachine>(key, trigger, input)` fires a trigger on a system instance
  from code, so a system instance can be retried by re-entry (`Failed` --Retry--> its invoking state) without any
  surface reaching it. It refuses the triggers a user's advance refuses (`outcome-bound`, `effect-bound`) and writes
  through the same outbox. Still no GraphQL operation advances a system instance; a host that wants one writes it
  under its own authorization. `SystemInstanceAdvanceTests` in `Trax.Mediator.Tests.StateMachine.Integration`.
- **2026-10-08**: The operator view lists an instance's invoked runs, and the operator's cancel ships, both through
  `IOperationsService` (`GetMachineInstanceRunsAsync`, `CancelMachineInstanceAsync`) for the dashboard and GraphQL
  (`machineInstance { invokedRuns }`, `cancelMachineInstance`). A run records the machine, instance id and owner
  kind that queued it, not the user, so a user's draft lists only its live run (the one its own token names), never
  every run under its id, which could be another user's; a system instance lists every run, at most 50. The cancel
  reuses the operator's cancel of a run (a conditional statement on the queued entry, else the run's cancel flag) and
  applies a queued run's outcome through `IInvokedRunOutcomes`, the reconciler's own delivery, so it is applied once.
  Its result is a typed outcome (`Moved`, `RunCancelled`, `CancelRequested`, or refused as `UserOwned`, `NotFound`,
  `NoLiveRun`, `RunEnded`) with one message on both surfaces. The race with dispatch and the cancel's end-to-end
  tests are `InvokedRunOperationsTests` in `Trax.Scheduler/tests/Trax.Scheduler.Tests.Integration`, on Postgres and
  SQLite, where the dispatcher and the reconciler are reachable; the surface tests (parity, the context, the
  authorization matrix) stay in `Trax.Api/tests`.
- **2026-10-08**: Only a system-owned machine chains runs through outcomes. A user-owned machine whose `OnDone`,
  `OnFailed` or `OnCancelled` enters an invoking state is refused at startup, naming the machine, the state, the
  outcome and the target, because the run would be queued with no user present to authorize it; it chains through a
  user event instead, which is authorized as that user. The launcher refuses a launch from an outcome for a
  user-owned instance as well, so the trusted scope is reachable only from a system-owned one. A token whose run can
  no longer be found (no work queue entry and no metadata row) goes to `OnFailed` with the reason
  `invoke-run-missing`, and metadata retention keeps an invoked run while an instance still holds its token.

- **2026-10-08**: A completed invoked run writes the output its machine reads to `metadata.invoke_output` (or marks
  it `invoke_output_oversize`) in its own terminal write, so once the run is recorded completed its output is
  durable and no crash point loses or doubles an outcome; the column is internal and on no operator surface. The
  hook on the run's host and the reconciler on every host that registers machines deliver through one conditional
  update; the sweep interval is `StateMachineOptions.InvokeOutcomeSweepInterval`, and Postgres migration 073's
  trigger wakes it. Fail-closed: a completed run whose output no `OnDone` accepts, or whose outcome cannot be
  applied, goes to `OnFailed` with a typed reason; if that cannot be applied either, the token is cleared and the
  reason logged. A run queued because an outcome entered an invoking state was authorized in the trusted scope on
  any machine, since no user is present; superseded the same day, see the entry above. `InvokeOutcomeDeliveryTests` live in `Trax.Scheduler/tests/Trax.Scheduler.Tests.Integration`,
  on Postgres and SQLite, where the dispatcher, the job runner and the reaper are reachable.

- **2026-10-08**: The launcher port lives in Effect.StateMachine.Persistence, because it writes through the data
  context the engine package does not depend on. A machine is system-owned by declaring `SystemOwned()`, which
  makes it system-only: `Start` refuses any other machine, and no user's draft operation reaches one. Because
  `IEffectJunction` is internal, an `IChain<I>` is checked by the class the container registers for `I`. The tests
  that need the mediator's launcher (`InvokesStartupRefusalTests`, `InvokedTrainOutboxTests`,
  `InvokesAuthorizationTests`) live in `Trax.Mediator/tests/Trax.Mediator.Tests.StateMachine.Integration`, on
  Postgres and Sqlite, since Effect's tests cannot reference the mediator; the scheduler's retry test is
  `InvokedRunIsNeverRetriedTests`.

- **2026-10-08**: An outcome target that itself invokes a train may be entered by an ordinary edge, so a chained
  stage can be retried; the builder method's signature names the train's input and output types.
- **2026-10-07**: Recorded.
