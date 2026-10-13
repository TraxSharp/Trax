---
authors: [Theauxm]
repos: [effect, mediator, scheduler, api, dashboard, cli, samples]
areas: [platform, graphql, data-model]
status: accepted
---

# A machine is served as a typed API, and the server applies every transition

A frontend drove a state machine through four generic mutations (`saveSnapshot`, `advanceSnapshot`, `loadSnapshot`,
`sendSnapshot`) shared by every machine: the event was a string and its input untyped JSON, a client could store any
snapshot it computed, any authenticated caller could send any event, and a change the server made (an invoked train's
outcome) was seen only by polling. Each exposed machine is now its own typed API: one mutation per event, a `get`
query and a `machineChanged` subscription, with per-event authorization checked on the server, trigger input validated
in both engines, every write numbered by a per-instance revision and pushed to its subscribers, and timers that move a
machine with nobody present. The client may predict a transition with the TypeScript twin, but the server applies it,
stores it and returns what it stored.

## Status

**Accepted.** Built in stages, each a pull request stacked on the one before. The tests listed under *Tests* were
written first, marked `[Red]` (they run, and pass only while they fail, so CI stays green and a test that starts
passing is noticed), and each stage removes the marker from the tests it turns green.

Amends [0046](./0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md) (the startup
comparison per entering event, the actor, timers and invoking states) and effect/0017 (autosave), each in its
Changelog.

## Why this is written down

Because every rule below is a place where a reasonable implementation is quietly wrong. A typed GraphQL input alone
leaves the generic path and host code unchecked; a role check in the resolver leaves every other surface open; a
revision read and written back by the application loses writes from old nodes and custom stores; a push that names a
user's instance by an argument shows it to anyone holding the same id; a document id without the IR's hash serves an
old client a new machine. Each decision closes one of these, and most of them fail closed: a machine nobody named is
not served, an event nobody declared a rule for refuses startup.

### The wire contract

**A `machines` root, one field per exposed machine.**
`mutation { machines { topicMap { start(input, requestId) submit(id, input, requestId) discard(id, requestId) } } }`,
`query { machines { topicMap { get(id) } } }` and `subscription { machineChanged(...) }`. Machines are their own kind
for the namespace and operation caps (`api/0029`) and in the authorization census. A separate root was chosen over
`dispatch { <machine> }` because a machine is not a train: its fields cannot be trains (a train needs a CLR type per
event), and the resolver dispatches the existing internal advance and send trains instead, so audit, metadata, error
mapping and the dashboard stay on one service path ([0022](./0022-the-dashboard-and-the-api-share-one-operation-per-action.md)).

**Exposure is explicit, and it matches trains.** A machine class marked `[TraxMachine]` is served, as a train marked
`[TraxMutation]` or `[TraxQuery]` is, so a type shows on its face whether it is part of the API. The attribute is the
only switch: there is no host-side call that serves an unmarked machine, and no mode that serves every machine, as
there is neither for trains. It is read from every machine the host registers, however it was registered, as train
discovery reads every train in the service collection: one the `AddStateMachines` scan found, and one the host
registered as `IMachine` itself, by its type, as an instance or through a factory. A host that would serve a machine
it does not own derives a class from it, marks that, and registers only the subclass: a subclass has its base's id,
every machine is looked up by its id, and a host that registers two machine types under one id refuses startup,
naming both and, when one derives from the other, saying to register only the subclass. The base must therefore not
be registered (not by a scan of its assembly, nor by a registration of the library's), and a sealed machine cannot be
served this way. The same type registered twice is one machine, served once. Nothing is served unless marked, and a marked type serves
exactly that class: the attribute is not inherited, and a host that registers an unmarked concrete subclass of a
marked machine refuses startup until the subclass is marked. On a type the scan finds that is not a concrete,
non-generic machine (an abstract or open generic machine, or a class that is no machine) the attribute refuses
startup rather than serve nothing. A closed generic machine the host registers by its closed type, from an assembly
it does not scan, is served by the attribute on its generic definition. `AddTraxGraphQL` gives the schema its `machines` root from the marked machines
registered before it by their type or as an instance; a marked machine it could not see (registered after it, or
through a factory) is served when the root is there, and refuses startup, naming it, when it is not. A host that
registers no machine at all refuses startup. `AddStateMachines` is called once; a second call refuses. Machines and
plain trains are served side by side: neither replaces the other. Exposing a raw-delegate machine (no IR, so no types and no hash) or a system-owned machine
refuses startup. Both stay usable from host code.

**No autosave on the typed surface.** A draft starts with an explicit `start`, which takes a declared
`StartInput` record and a declarative reduction into the context, never the context type itself; startup refuses a
start that reaches a field an outcome, an effect, a gated event or a timer writes. Every later change is a declared
event. The server mints the instance id.

**The generic mutations are opt-in.** `ExposeGenericStateMachineMutations(o => o.Policy = ...)` registers the six
generic mutations (`startSnapshot` and `discardSnapshot` beside the four above) behind a policy gate, and the opt-in without a policy refuses startup. Registering the internal trains'
routes is separated from exposing them in GraphQL, which is the same attribute today. On the opt-in path the generic
save serves only machines with no server-side behaviour: a machine with a gated event or a gated `start`, a timer, an
invoked train or an effect answers `save-not-allowed`. Reserving each state such a machine's server owns, one by one, was the
alternative; a machine like that has typed events to move it, and the save is the legacy path. This ships as `feat:`,
not a major (sole consumer).

**Payload types are C# records.** `WithInput<T>()` keeps `typeof(T)`, the GraphQL input type is built from the CLR type
as a train's is, and the IR schema the twin reads is exported from it. A declarative event without `WithInput` gets a
mutation with no input argument, never a JSON scalar. `IMachine.Triggers` lists every trigger with its kind (client,
effect-bound, outcome, timer) and input type, which is what the GraphQL module reads.

**An effect-bound event is an ordinary event mutation.** The server knows the event is bound to the machine's
`RunsOnce` effect and runs it, so the client never sees the save, advance and send split.

**`requestId` is required on every event, `start` and `discard`.** Each row keeps a ring of its last client request
ids with the result recorded for each, up to sixteen, and at most 128 KiB of recorded results, counted as the JSON
text the column stores (the oldest go first); a retry replays the recorded result, never the current snapshot, and server
writes (outcomes, timers) never evict an entry. One request id with a different trigger or input is
`request-id-reused`. `start` has its own `start_request_id`, unique per owner kind, user key and machine, with an input
fingerprint, so a retried `start` returns the same instance. A request id that has left the ring cannot be told from a
new one, so it is handled as new: it fires again under every rule above, and from a state the machine has since left it
is a `no-transition`. Remembering every id forever was the alternative, and costs a row per event. The client's hook
serialises events per instance, so a retry is never 16 events stale.

**`clientResult` stays optional, and a mismatch is refused** as `client-divergence`, as it is today, with a meter
counting it (twin drift). Its size is capped, and only its hash and length are logged. A replayed request is judged
against its recorded result, not against a snapshot written after it.

**One event per operation.** HotChocolate runs fields nested under a namespace in parallel, so an operation with more
than one machine event field is refused before any runs.

### Input validation

**The server validates trigger input, in both engines, against one exported schema as strict as the CLR type.**
Unknown fields are refused (a trigger without `WithInput` accepts only `null` or `{}`), the exported schema has a real
`integer` kind, `[JsonPropertyName]` is honoured, the size is capped before parsing, and a recursive input type or two
edges declaring different input types for one trigger refuse to build. Constraints on an input are checked against the
input, never the context. The refusal is `invalid-input`, as data, never an exception. Validation sits in the engine,
not the resolver, so the generic path, host code and operator paths are checked too.

**One engine contract.** Both engines refuse an outcome or timer trigger sent through `advance` (`outcome-bound`,
`timer-bound`) and apply them through separate entry points (`ApplyOutcome`, `AdvanceTimer`). The TypeScript twin
applied outcomes through `advance`; it now matches.

**Refusal precedence**, first match wins, on the draft service's path, which the typed operations, the generic
opt-in and host code acting for a client all share: a timer trigger (`timer-bound`); a request id the row cannot record
(`invalid-request-id`) or an input past the size cap (`too-large`); an id the caller holds no instance under
(`not-found`); an effect- or outcome-bound trigger; `forbidden`; the request-id ring (a replay, or
`request-id-reused`); then the engine's order: an unknown trigger (`no-transition`), no edge from the current state
(`no-transition`), `invalid-input`, `guard-failed`, `invalid-context`. The request's shape and the instance's existence
come before the caller's rule, so another user's id is `not-found`, the same answer as a missing one, never
`forbidden`. A caller is told whether they may send an event before anything about its input, and a replay is checked
against the caller's rule as it is now, so a caller who lost the role is `forbidden`, not replayed. The ring comes
before the engine's unknown-trigger check, so a request id reused for an unknown trigger is `request-id-reused`.

### Authorization

**Every client event declares a rule:** `Requires(roles)`, `RequiresPolicy(name)`, `AnyAuthenticated()` or
`Anonymous()`. An undeclared event refuses startup, every undeclared one named in one refusal, and there is no
machine-level default. A rule is required on every machine a client can reach: one marked `[TraxMachine]`, or every
user-owned machine while the generic opt-in is on. Reads have rules too: `start`, `get` and `machineChanged` each
declare one (`Start(...)`, `Get(...)`, `Watch(...)`), owner-only is the floor, and a looser one refuses startup.
`discard` declares one as the reads do (`Discard(...)`, owner-only the floor), and is refused as
`discard-not-allowed` from a row the server put in its state, from an invoking state, and while an effect is pending
on the row (see *Effects and server records*). The delete is conditional on the row id, revision and invoke token it
checked.
`Anonymous()` on a user-owned machine refuses startup until an anonymous-session owner is designed.

**"Gated" means stricter than `AnyAuthenticated`:** a rule that names a role (`Requires`) or a policy
(`RequiresPolicy`). A gated event into the start state refuses to build, and what a gated event writes counts as a
server write for the start's reach check. An event open to any caller (no rule, `AnyAuthenticated` or `Anonymous`)
whose reduction copies from its input a field any server write writes (a gated event, an effect's receipt or an
invoked train's outcome) refuses to build too, on a machine that declares rules; a custom reducer counts as copying
every field, except one the event's target state's declared invariant requires to be absent, which the invariant
refuses there. Two edges of one event with
different rules, or a rule on an outcome or timer trigger, refuse to build.

**Rules are checked below the resolver**, in the advance and send path where every surface meets, against an explicit
actor, `Client(principal)`, the only kind (see the Changelog, 2026-10-12): host code on a user's instance is a client
holding the roles it passes, and host code drives a system-owned instance through `IMachineInstances`, the named host
API. So the generic opt-in, a host train forwarding a caller's trigger, and the
effect-bound send (which names no trigger) are all checked. `ISnapshotPrincipal` carries only a user key, so roles and
policies are evaluated through the mediator's authorization, per event, never cached on the draft.

**Host code on a system-owned instance is trusted** and skips per-event rules, but never input validation, and the
census lists which events code can send. **Host code on a user's instance is a client** holding the roles it passes,
held to every rule; there is no trusted path to a user's instance. **The census** lists each event and read with its rule and the surfaces that reach it; outcome and timer
triggers are listed as server-only.

**The startup comparison of 0046 is per entering event.** The strictest requirement of an invoked train is compared
with each event that enters the invoking state, including `start` into an invoking initial state; a policy rule
against a role requirement cannot be proven and is refused. An `Operator` event may now invoke an `Operator` train.

### Revision and push

**A per-instance `revision`, bumped by the database.** A `BEFORE UPDATE` trigger (Postgres and Sqlite) sets
`revision = OLD.revision + 1`, so old nodes during a rollout, custom stores and every write path are covered without
each remembering to. A compare-and-swap write reports `stored + 1` without reading back. A delete then re-create under
one id is a new `row_id` (the incarnation), which restarts at 1. The row also stores the IR hash that wrote it.
The EF InMemory provider has no triggers, so there the store bumps the revision itself in each write.
`revision` and `dueAt` travel beside the snapshot on every result, never inside the canonical wire that `clientResult`
and the replay fingerprint hash. A null `dueAt` means no timer, or one the server cleared; a replay the row has moved
past cannot know the due time and says `dueAtKnown: false` instead.

**Each push is the full snapshot with its revision**: `{rowId, revision, state, context, dueAt}` for the subscriber's
own instance, and a terminal `gone` on delete or expiry. The client drops anything at or below the revision it holds,
its own mutation results included, so a skipped revision is harmless and never a reason to reload. It reloads only on a
listener reconnect, an incarnation change or `client-outdated`. The subscriber registers before the initial read, and
the first event is the current snapshot.

**The source is a Postgres trigger on `snapshot_draft`** that sends `NOTIFY trax_snapshot_changed` with the `row_id`
only. The payload is a hint: the revision and deletion are read from the database, so a notice sent by hand moves nothing,
and a malformed one is metered. Each host runs one listener that never holds a transaction, re-reads a row once and
only if it has subscribers, coalesced to the latest revision, and reports `pg_notification_queue_usage()` for alerting.
Sqlite and the InMemory provider poll the watched rows (an operator feed on one machine watches that machine's rows,
one on every machine the whole table), only while something watches, and so does a Postgres host that chooses
`ChangeFeed = Poll`: every notifying transaction takes one cluster-wide lock held through its commit, so once every
host polls the database may turn the snapshot notices off for every session (`ALTER DATABASE ... SET
trax.snapshot_notify = 'off'`). That is the only supported switch, because the trigger reads it in the writing
session, which a listening host cannot see. A snapshot write in a transaction that sends another notice (a work queue
entry, an invoked run) still takes the lock.
Contexts never travel through RabbitMQ or SignalR. Publishing from each write
path was the alternative, and misses writes made on hosts without sockets (outcomes are applied wherever the train ran
or the reconciler swept).

**The topic key comes from `ISnapshotPrincipal`, never from an argument**, because two users can hold the same id. A
subscription is verified once against the read rule, then routed by `row_id`; a missing id, another user's id and a
system instance's id get the same answer. Subscriptions are capped per principal and per instance, token expiry ends
the stream, and the socket rules (origin, operation cap) are unchanged. **Clients never subscribe to system-owned
instances.** The operator instance view, in GraphQL and Blazor, gets live state, revision and runs from the same feed
under the operations gate, never the context, and the Blazor view re-checks the operator within a bounded circuit
lifetime.

### Versioning and persisted documents

**Persisted, required.** A host that exposes a machine without persisted operations refuses to start: the stored
documents are what order a machine's versions across nodes, and without them a node could only guess whether a row
was written by a newer version. After the schema builds, Trax.Api generates each exposed machine's documents and upserts
them idempotently across nodes, under the null tenant, with no history row or broadcast for an unchanged document.
The id lives under a reserved prefix that upload, deactivate and restore refuse, and hashes the generated document
text together with the IR; a lookup verifies the stored body against its id, and startup repairs a mismatch and
alerts. A query exports the documents as the manifest a generated client reads, behind the operations gate. The CLI
never needs field names (it may not reference Trax.Api).

**Versioning fails closed, and rolling deploys are not reloads.** A node serves machine documents only for its own
hashes: an older id answers `client-outdated` (the client reloads, and an open stream ends with the same problem), a
newer one answers `server-outdated`, which is retryable. An id of a machine the node does not serve is answered from
the store: `server-outdated` while the store holds it active, `client-outdated` once it was collected. An id the node
has not seen is answered from its last read of the store without waiting for one: `server-outdated` until a read
that began after it was first asked for lacks it, then `client-outdated`; the ids waiting for such a read are bounded,
the oldest forgotten first. A flood of more made-up, well-formed ids than the bound within one read interval keeps a
real stale client on `server-outdated` while it lasts. That is accepted: the flood is bounded in memory and in reads,
runs nothing, and its effect ends with it. Bounding the waiting ids per client or per machine was the alternative,
and protects nothing, since the caller may be anonymous and the machine segment of the id is the caller's to choose. Neither answer is a server error: `client-outdated` is HTTP 400 and `server-outdated` 409,
and the client reads the code. Old ids are collected only after a grace period, by a registration every running
node repeats on a timer. A registration marks the documents of a machine the registering node does not serve
`unexposed:`, a node serving the machine clears the mark, and a document still marked after the grace period is
collected. A node that serves no machine registers an empty set of documents the same way, so a deploy that stops
serving every machine still has the older documents collected and their clients reload; a failed registration does
not stop such a node, which retries at the next interval. A collected document is made active again only at a node's startup or when no newer version of its
machine is stored. The row's stored IR hash ends an older node's stream when a newer version writes the row. This
replaces the optional, fail-open `schemaHash` handshake.

### Timers

**`.State(S).After(TimeSpan)` or `.After(c => c.Field, min, max)`, then `.GoTo(T)`.** Declarative, so the IR exports
it as a server-only trigger kind; at most one per state; started on entry and cancelled on leaving. A duration read
from the context is read at entry and refused as `invalid-input` outside its declared bounds; a delegate duration
cannot be exported and is refused, and so is a duration field marked `[TraxSensitive]`, because the deadline is shown.
A self-loop timer and a timer-only cycle refuse to build.

**Firing.** Entering a timed state writes `due_at` from the database clock in the same statement, on every entry path
(start, advance, outcome, upsert, system, effect); leaving clears it, and a stale `due_at` from a removed timer is
cleared. The 0046 reconciler on every machine host, now also on hosts whose machines only have timers, sweeps due rows
and fires each in a transaction of its own that locks the row first (`SKIP LOCKED`), applying the timer by a
conditional update on state and revision, so a timer and a client event cannot both win. A timer waits while an
effect is pending on the row, and the fire's conditional update requires that none is, so an effect and a timer
never both apply. A fire that cannot be applied clears `due_at` and is metered; one
that throws rolls back alone, backs off, and is cleared after a bounded number of attempts, counted per host in
memory. A database failure (unreachable, out of resources, shut down, timed out, or a transaction rolled back for a
deadlock or a serialization failure, by its SQLSTATE class; a provider error with none) is backed off but is not an
attempt, so a failing database never clears a timer, while a failure about the row's data is an attempt.

**A client can never send a timer trigger**, and neither can host code through `advance`. The twin shows the deadline
and never fires it. A timer's target is not reserved, because a timer carries the context unchanged; the timed state instead is
locked, so autosave cannot leave or rewrite it. A timer edge carries the union of the rules of the events entering its
source state, for the census and the startup comparison. On a user-owned machine a timer into an invoking state refuses
startup (nobody is present to authorize the run); a timer leaving an invoking state goes through the outbox's leaving
path. A pending timer keeps a user draft past its time-to-live, up to `MaxDraftLifetime`. Neither the time-to-live
nor `MaxDraftLifetime` deletes a row the server put in its state: that is the record of what the server did, and the
host decides how long it stays. `MaxDraftLifetime` deletes every other user draft created longer ago, one waiting on
an invoked run with its run cancelled first, and passes over a row with a pending effect.

### Effects and server records

**An effect is pending on its instance's row, not beside it.** Sending an effect-bound event marks the row, by one
conditional write on its state and revision, with the effect's key, the claimant and a lease. While the mark is there,
no other write moves or changes the row: an event is refused as `effect-in-progress`, a due timer waits, a reset and
the generic save are refused, a discard is refused and the draft sweep passes the row over. Each of those already
writes conditionally on the revision, so the mark needs no separate check that could be read and then acted on late.
The effect's receipt, the move to its target and the end of the mark are one write; an effect that throws ends the
mark and leaves the row as it was. An effect that ran is therefore either still pending on its row or recorded in it.

**A pending effect is always finished.** The mark carries a key minted when the row is marked. Once its lease has
passed on the database clock, the mark is taken over by the next send of the same event or by the reconciler on a host
that registers the effect, and either runs the effect again under the mark's key and records the result. An effect
must be safe to run again under its key, as the lease already required; the key is what it passes to the system it
calls so that a repeat is recognised, and the next send of the event, after a reset for instance, is a new intent with
a new key. A host without the effect leaves the row, and a mark past its lease is metered. An effect neither runs from
nor lands in a state that invokes a train, which refuses to build: the outcome of the train would have to be applied
to a row the effect holds.

**A row records whether the server put it in its state.** An effect's write, an invoked train's outcome and a gated
event set the record; a client event clears it; a timer carries it over, since a timer moves what it found. Such a row
is refused by discard, kept past `DraftTtl` and `MaxDraftLifetime`, and not counted toward `MaxDraftsPerUser`: each
one cost a server action, so the cap still bounds what a client alone creates. The kind of state no longer decides
this, so a state that both a client and an operator can enter keeps only the rows the operator moved there. Rows
written before this rule are marked kept when their state is one the earlier rule kept, and a completed claim from
the earlier ledger whose row is still in the effect's state is recorded by that row's next send.

**What holds after every write**, and what the interleaving test checks after every step:
- an effect that ran is recorded on its row, or its row still carries its mark;
- no row holds two outcomes for one effect, its target and a timer's, or two receipts;
- the rows a user holds that the server did not put in their state number at most `MaxDraftsPerUser`;
- a row with a pending effect is changed only by that effect;
- every mark is finished or metered within its lease and one reconciler interval.

### Scope

Shared instances (several users moving one instance) are a later decision; nothing here precludes them: the per-event
rules, server-minted ids and the subscription key all leave room for participants. A transition log is a separate
decision too, so the operator view shows the current state, revision and runs, not history. The decision is proven by the
Recovery sample's topic map, driven from a browser through the typed surface only, with the generic mutations off and
ad-hoc documents refused ([0049](./0049-browser-tests-use-playwright-for-dotnet-in-test-projects-only.md)).

## Considered options

**Typed mutations generated by the CLI as trains.** Rejected: the CLI may not reference Trax.Api, cannot know field
names, and a train needs a CLR type per event.

**A role check in the resolver.** Rejected: every other surface (the generic opt-in, host trains forwarding a caller,
the effect-bound send) would stay open.

**Exposure only in the host (`Expose<TMachine>()`), or one `[TraxEndpoint]` for trains and machines.** The first
was built first: it left whether a machine is served as the one fact not on its class, beside the rules that are, and
unlike a train. The second would blur the train attributes, each of which names one operation of one kind, with a
machine, which is a group of operations. `[TraxMachine]` with `Expose` kept for machines a host does not own was
chosen first, and then `Expose` was removed before it was released: a train has no host-side switch, so neither does a
machine.

**An expose-all mode (`ExposeAllMachines()`).** Rejected: trains have none, and it would serve a machine whose class
does not say so, including one added later to a scanned assembly.

**A machine-level default rule.** Rejected: a new event would inherit it silently. Declaring each is the census.

**Logging a `clientResult` mismatch without refusing it.** Decided first, then reversed: the refusal is what keeps the
twin honest, and refusing is what the code already did.

**A gap in the revision means reload.** Rejected for ordering: every event is a full snapshot, so the newest wins and a
lost one changes nothing; HotChocolate's bounded buffer drops the oldest event silently anyway.

**Publishing snapshot changes from the application.** Rejected for the database trigger (above).

**Reserving each gated or server-written state from the generic save.** Rejected for refusing that save on any machine
with server-side behaviour (above).

**An effect claim in a ledger beside the row, with rules for when its result has reached the row.** What was built
first. The timer, discard, the draft sweep and a reset each needed to know whether an effect's result had reached the
row, and each inferred it from the ledger its own way. Replaced by the mark on the row, which every writer already
meets through its revision condition; the ledger stays for `IdempotentEffect` outside machines.

**An "applied" flag on the ledger's claim, set in the advance's transaction.** Rejected: every writer would have to read the
ledger too, where a mark on the row reaches each through the revision condition it already has.

**Firing a timer once its state's effect is past its lease.** Rejected: the effect may have happened, and the
instance would then hold both outcomes. Finishing the effect under its key is what frees the timer.

**Keeping a row by the kind of state it is in.** What was built first. Rejected: whether the server acted is a fact
about the row, not its state, and a state that a client and the server can both enter is where the two differ.

## Tests

Written first, red, before the code they cover, each marked `[Red]` until its stage turns it green; a test noted as a guard passes today and must keep passing. Each names the class and method it has. Tests added with a later amendment (see the Changelog) are listed with their class.

In `Trax.Api.Tests.StateMachine.E2E`:
- `GenericOptInE2ETests`: `Without_the_opt_in_the_schema_has_no_stateMachine_field`, `With_the_opt_in_the_gate_policy_is_enforced_on_all_six`, `The_opt_in_without_a_policy_refuses_startup`, `A_host_with_unexposed_raw_delegate_machines_starts_and_serves_the_generic_opt_in`
- `MachineChangedE2ETests`: `The_machineChanged_document_is_registered_under_the_reserved_prefix`, `It_runs_by_id_over_the_socket`, `The_subscriber_registers_before_the_initial_read_and_the_first_event_is_the_current_snapshot`, `Revisions_arrive_in_order_never_repeating_or_decreasing`, `A_burst_is_coalesced_to_the_latest_revision_and_a_skipped_revision_triggers_no_reload`, `A_server_made_outcome_applied_on_another_host_reaches_the_subscriber`, `A_missing_id_another_users_id_and_a_system_instance_id_get_the_same_answer`, `The_read_rule_is_checked_once_at_subscribe_and_routing_follows_row_id`, `A_principal_whose_read_rule_fails_is_refused_at_subscribe`, `Delete_yields_gone_without_a_re_read_and_completes`, `Expiry_by_the_sweeper_yields_gone`, `A_recreate_under_the_same_id_does_not_reach_a_stream_on_the_old_incarnation`, `Token_expiry_ends_the_stream`, `The_user_key_comes_from_the_socket_principal_under_jwt_and_api_key`, `An_older_hash_subscription_id_is_refused_client_outdated`, `A_row_written_by_a_newer_ir_ends_an_older_nodes_stream_client_outdated`, `Subscriptions_are_capped_per_principal_and_instance`, `machineChanged_counts_toward_the_100_operation_socket_cap`, `An_unlisted_origin_cannot_upgrade_for_machineChanged` (a guard, green today), `An_aborted_socket_releases_its_instance_watch`, `An_anonymous_socket_is_refused`, `A_forged_notice_for_an_unchanged_row_sends_nothing`, `N_subscribers_and_M_notices_on_one_row_cost_one_coalesced_read`, `A_row_the_host_cannot_parse_ends_that_stream_only`
- `MachineClientResultE2ETests`: `A_mismatched_clientResult_is_refused_as_client_divergence_and_counted`, `A_replayed_request_does_not_falsely_diverge`, `ClientResult_is_capped_and_only_its_hash_and_length_are_logged`
- `MachineDocumentCrossNodeTests`: `Two_nodes_starting_together_register_each_document_once_and_both_start`, `Document_text_is_identical_across_processes`, `A_graphql_naming_change_without_an_ir_change_yields_a_new_id`, `After_an_ir_change_the_old_id_answers_client_outdated_and_the_new_one_works`, `A_node_serves_machine_documents_only_for_its_own_hashes`, `A_newer_hash_on_an_older_node_is_server_outdated_and_retryable`, `A_client_bouncing_between_old_and_new_nodes_never_gets_client_outdated_for_a_newer_hash`, `Startup_never_deactivates_an_older_irs_documents`, `Old_ids_are_collected_only_after_the_grace_period`, `A_running_node_collects_old_ids_after_the_grace_period_without_a_restart`, `A_machine_exposed_only_on_a_newer_node_is_server_outdated_on_an_older_one`, `Adding_a_timer_changes_the_document_ids`, `A_machine_a_newer_deploy_stopped_exposing_is_client_outdated_once_its_documents_are_collected`, `A_machine_a_newer_node_serves_is_never_collected_by_an_older_node_that_does_not`, `An_older_node_past_the_grace_period_does_not_reactivate_what_a_newer_node_collected`, `A_deploy_that_serves_no_machine_still_collects_the_old_documents_and_old_ids_turn_client_outdated`
- `MachineDocumentRegistryTests`: `An_unseen_id_is_answered_at_once_and_concurrent_misses_never_wait_on_each_other`, `An_id_the_store_still_lacks_after_a_later_read_is_client_outdated`, `Misses_read_the_store_at_most_once_per_interval`, `A_stored_active_id_of_a_machine_this_node_does_not_serve_is_server_outdated`, `A_collected_id_of_a_machine_this_node_does_not_serve_is_client_outdated`, `An_unseen_id_of_a_machine_this_node_does_not_serve_is_client_outdated_once_a_later_read_lacks_it`, `A_flood_of_unseen_ids_forgets_the_oldest_and_keeps_an_id_asked_for_after_most_of_it`
- `MachineDocumentsE2ETests`: `Every_machine_document_is_registered_at_startup_under_the_reserved_prefix`, `Restarting_a_node_writes_no_history_and_broadcasts_nothing_for_unchanged_documents`, `A_lookup_whose_stored_body_does_not_match_its_id_is_refused`, `Startup_repairs_a_tampered_machine_document_and_alerts`, `Machine_documents_are_registered_under_the_null_tenant_and_a_tenant_row_does_not_shadow_them`, `A_request_arriving_before_registration_completes_does_not_pin_a_miss`, `Unknown_reserved_prefix_ids_read_the_store_at_most_once_per_refresh_interval`, `A_well_formed_made_up_id_is_answered_without_a_server_error_status`, `The_manifest_query_lists_every_document_with_its_hash_behind_the_operations_gate`, `A_recursive_input_or_context_type_refuses_startup_rather_than_generating_forever`
- `MachineErrorMappingE2ETests`: `An_infrastructure_failure_inside_advance_is_a_typed_problem_without_server_detail`
- `MachineEventsE2ETests`: `Each_event_runs_through_the_advance_train_and_returns_the_servers_snapshot_with_revision`, `Internal_machine_trains_are_not_broadcast_on_the_lifecycle_feed`, `Invalid_input_is_refused_as_invalid_input_and_nothing_is_written`, `A_refused_guard_is_data_not_an_error`, `An_events_sensitive_input_field_is_masked_in_the_runs_recorded_input`, `An_operation_with_two_machine_event_fields_is_refused`, `Two_aliased_gets_in_one_request_share_the_scoped_DbContext_safely`
- `MachineRegistrationE2ETests`: `A_base_machine_registered_beside_its_marked_subclass_refuses_the_host_naming_both`, `The_same_machine_registered_twice_starts_serves_it_once_and_every_machine_answers`
- `MachineRequestIdE2ETests`: `RequestId_is_required_on_every_event_start_and_discard`, `An_effect_bound_event_runs_its_effect_once_under_retries_with_one_requestId`, `The_entering_event_queues_one_run_under_retries`, `One_requestId_on_two_different_events_is_request_id_reused`, `An_event_retried_after_a_server_write_replays_from_the_ring`, `Twenty_concurrent_starts_with_one_requestId_create_one_instance`, `A_start_retry_with_a_different_input_is_request_id_reused`
- `MachineRulesE2ETests`: `A_caller_without_the_events_role_is_forbidden_and_nothing_is_written`, `The_effect_bound_event_checks_its_rule_before_claiming_the_effect`, `The_generic_opt_in_advance_checks_the_same_event_rule`, `Get_of_another_users_id_is_answered_like_an_unknown_id`, `An_operators_role_does_not_unlock_another_users_draft_through_machines`, `Discard_of_an_instance_the_server_moved_is_discard_not_allowed_and_keeps_it`, `Discard_follows_whether_the_server_or_its_owner_put_the_instance_in_its_state`
- `MachineRunRecordE2ETests`: `A_machine_event_run_row_stores_no_output_and_a_plain_train_still_does`, `The_generic_save_stores_its_snapshot_masked_and_no_output`, `The_operators_execution_detail_reads_a_machine_run_with_no_output`
- `MachinesRootSchemaTests`: `Machines_root_lists_each_exposed_machine_with_start_events_and_get`, `An_unexposed_declarative_machine_gets_no_field`, `An_event_without_WithInput_has_no_input_argument_and_no_json_scalar`, `Outcome_timer_and_effect_receipt_inputs_never_appear_as_client_arguments`, `Start_takes_its_declared_StartInput_never_the_context_type`, `A_misspelt_event_field_is_a_validation_error_not_a_runtime_problem`
- `OperatorMachineFeedE2ETests`: `The_operator_subscription_is_gated_and_shows_state_revision_and_runs`, `A_users_draft_change_appears_to_an_operator_without_its_context`, `Each_host_runs_one_listener_shared_by_the_dashboard_and_GraphQL`, `After_a_listener_reconnect_the_operator_feed_sends_a_reload`

In `Trax.Effect.StateMachine.Persistence.Integration`, on Postgres and SQLite:
- `EffectPendingTests`: `An_event_out_of_the_effects_state_while_it_is_pending_is_refused_effect_in_progress`, `A_reset_and_the_generic_save_are_refused_while_an_effect_is_pending`, `Discard_and_the_draft_sweep_leave_a_row_with_a_pending_effect`, `A_due_timer_waits_for_a_pending_effect_and_fires_after_it_fails`, `The_receipt_the_move_and_the_end_of_the_mark_are_one_write`, `A_failed_effect_leaves_the_row_as_it_was_and_takes_events_again`, `A_mark_past_its_lease_is_finished_by_the_next_send_under_the_same_key`, `A_mark_past_its_lease_is_finished_by_the_reconciler_under_the_same_key`, `A_host_without_the_effect_leaves_an_abandoned_mark_and_meters_it`, `A_completed_ledger_claim_from_before_the_upgrade_is_recorded_by_the_next_send`, `A_send_after_the_effect_was_recorded_and_the_draft_reset_runs_under_a_new_key`
- `ServerRecordTests`: `A_row_a_gated_event_moved_is_kept_and_refuses_discard`, `A_row_a_client_event_moved_into_the_same_state_is_a_draft`, `A_client_event_out_of_a_kept_row_makes_it_a_draft_again`, `A_timer_carries_the_record_over`, `Kept_rows_do_not_count_toward_the_draft_cap`, `The_upgrade_marks_rows_in_kept_states_as_kept`, `A_host_upsert_out_of_a_kept_row_leaves_it_judged_by_its_state`, `A_host_update_out_of_a_kept_row_leaves_it_judged_by_its_state`
- `MachineInterleavingModelTests`: `Seeded_interleavings_keep_every_invariant` (starts, events, sends, timers, discards, sweeps and a claimant that stops, in random order from a logged seed, with the invariants of *Effects and server records* checked after every step)

In `Trax.Effect.StateMachine.Tests`:
- `EventWriteRuleTests`: `An_open_event_copying_a_field_an_effect_writes_refuses_to_build`, `An_open_event_copying_a_field_an_invoked_runs_outcome_writes_refuses_to_build`

In `Trax.Api.Tests.Stress`:
- `MachineChangedStressTests`: `A_thousand_subscribers_and_ten_thousand_writes_stay_bounded`
- `MachineEventStressTests`: `Per_event_cost_is_one_metadata_row_one_audit_entry_and_one_notice`

In `Trax.Api.Tests`:
- `MachineExposureCensusTests`: `The_GraphQL_exposure_census_covers_machines_as_their_own_kind`, `The_census_lists_the_timer_as_server_only_with_its_union_rule`
- `MachineExposureStartupTests`: `Exposing_a_raw_delegate_machine_fails_startup_naming_it`, `Exposing_a_system_owned_machine_fails_startup_naming_it`, `Exposing_a_machine_mixing_delegate_guards_with_rules_fails_startup`, `Anonymous_on_a_user_owned_machine_fails_startup`, `An_undeclared_read_rule_on_get_or_start_fails_startup`, `Exposing_a_machine_without_persisted_operations_fails_startup_naming_the_fix`
- `MachineChangedOrderTests`: `Without_an_ir_order_another_hash_keeps_the_stream`
- `MachineInstanceQueriesOverHttpTests`: `No_operator_feed_type_has_a_context_or_owner_key`
- `MachineDocumentIdTests`: `An_id_names_only_the_machine_whose_id_is_its_whole_machine_segment`, `An_operation_holding_an_underscore_leaves_the_id_with_its_own_machine`, `No_id_is_generated_for_a_machine_id_holding_an_underscore`, `An_id_outside_the_prefix_or_without_a_machine_segment_names_no_machine`
- `MarkedMachineSchemaTests`: `A_marked_machine_has_its_field_in_the_schema`, `A_marked_machine_the_host_registers_itself_has_its_field_in_the_schema`, `A_marked_machine_registered_through_a_factory_is_served_beside_one_the_schema_saw`, `A_marked_machine_only_a_factory_registers_refuses_the_host_rather_than_go_unserved`, `An_unmarked_subclass_of_a_marked_machine_refuses_the_host_and_is_not_served`, `A_base_machine_registered_beside_its_marked_subclass_refuses_the_host_naming_both`, `The_same_marked_machine_registered_twice_starts_and_is_served_once`
- `MachineTypeModuleTests`: `CreateTypes_TwoMachinesShareAnInputRecord_RegistersItOnce`, `CreateTypes_MachineInputRecordAlsoATrainInput_DoesNotCollide`, `MachineNames_ThatNormaliseToOneField_FailStartupNamingBoth`, `AnEventNamedStartDiscardOrGet_FailsStartup`, `GeneratedTypeNames_DoNotCollideWithAClrTypeOfTheSameName`, `AHostContributedMachinesRootField_FailsStartup`, `MachinesFields_DeclareTheirPosture_UnderTheCensus`, `Census_ListsEachEventAndReadWithItsRule_ForTheSurfaceAndForHostCode`, `No_mutation_exists_for_a_timer_trigger`
- `OperationCapUnderNamespacesTests`: `FiftyOneAliasedGetsUnderMachines_AreRefusedAtTheDefaultCap`, `MachinesAndMachineFields_AreNamespaceFields_NotCounted`
- `PersistedOperationMutationTests`: `Upload_deactivate_and_restore_refuse_the_reserved_prefix`
- `PersistedOperationTransportTests`: `An_ad_hoc_machine_document_is_refused_on_every_transport` (a guard, green today)
- `SchemaRebuildTests`: `A_second_provider_builds_the_same_machines_schema`

In `Trax.Cli.Tests`:
- `MachineGeneratorNodeTests`: `Generate_reproduces_the_committed_reminder_artifacts_with_timers_byte_for_byte`
- `Placed`: `Text_marks_a_timer_edge_with_its_duration`, `Mermaid_marks_a_timer_edge_with_its_duration`, `Dot_marks_a_timer_edge_with_its_duration`

In `Trax.Core.Tests.Meta`:
- `InteractionMatrixTests`: `TheMachineApiFeaturesTable_CoversEveryMachineApiFeature_WithTestsThatExist` (a guard, green today), `AMachineApiFeatureWithoutARow_Fails` (a guard, green today)

In `Trax.Dashboard.Tests.Integration`:
- `StateMachineLiveViewTests`: `The_instance_page_updates_from_the_feed_without_a_poll_tick`, `The_page_and_GraphQL_read_one_feed_service`, `Leaving_the_page_disposes_its_feed_subscription`, `A_circuit_whose_operator_loses_the_role_stops_receiving_within_the_bounded_lifetime`

In `Trax.Effect.Data.Testing.Tests`:
- `TraxInvariantsTests`: `ARevisionBelowOne_IsReportedWithItsRow`, `AUserDraftWithAnEmptyOrBlankOwner_IsReportedWithItsRow`, `TwoRowsWithOneRowId_AreReported`, `ADueTimeInAStateWithNoTimer_IsReportedWithItsRow`, `ATimedStateWithNoDueTime_IsReportedWithItsRow`, `ADueTimeOfAMachineWithNoTimerLeft_IsReportedOnceTheHostsStop`, `AUserDraftOlderThanTheMaximumLifetime_IsReportedWithItsRow`

In `Trax.Effect.StateMachine.Persistence.Integration`:
- `ActorHierarchyTests`: `A_record_outside_Trax_cannot_derive_from_Actor`, `A_client_is_built_outside_Trax`, `Actor_offers_no_trusted_kind`
- `AddStateMachinesBuilderTests`: `Without_the_opt_in_the_internal_trains_route_but_are_not_exposed`, `A_second_AddStateMachines_call_is_refused_rather_than_replacing_the_first`, `A_host_that_registers_no_machine_refuses_to_start`, `A_machine_the_host_registered_before_the_scan_finds_it_is_registered_once`, `The_same_machine_type_registered_twice_is_one_machine_to_the_registry`
- `ClientDivergenceMeterTests`: `A_client_divergence_is_refused_and_metered`
- `DiscardRuleTests`: `Discard_from_a_state_only_the_server_reaches_is_refused`, `Discard_from_a_state_a_client_reaches_deletes`, `Discard_is_held_to_its_rule`, `A_machine_with_rules_but_no_discard_rule_refuses_discard`, `Discard_after_an_operator_approved_the_instance_is_refused`
- `DiscardEffectTests`: `Discard_is_refused_while_an_effect_on_the_instance_is_running`
- `DraftLifetimeTests`: `The_lifetime_keeps_an_instance_only_the_server_moved`, `The_lifetime_deletes_a_draft_waiting_on_its_run_and_cancels_the_run`, `A_row_without_a_creation_time_counts_its_age_from_its_last_write`, `The_sweep_keeps_a_draft_an_effect_is_running_on_until_the_effect_settles`, `The_sweep_releases_a_settled_claim_with_the_draft`, `A_ttl_longer_than_the_lifetime_refuses_to_start`
- `DiscardTests`: `Discard_of_another_users_id_answers_as_a_missing_one`, `Discard_of_a_system_instance_is_refused`, `Discard_is_idempotent`, `Discard_of_a_draft_holding_a_live_token_cancels_its_run_first`
- `DraftWriteRulesTests`: `The_generic_save_is_refused_for_a_machine_with_a_Requires_rule`, `The_generic_save_still_works_for_a_plain_machine` (a guard, green today)
- `EventAuthorizationTests`: `A_client_without_the_role_gets_forbidden_and_nothing_is_written`, `Event_rules_across_roles_and_surfaces`, `A_trusted_actor_is_not_held_to_event_rules_but_its_input_is_still_validated` (a guard, green today), `Host_code_on_a_users_draft_is_a_client_holding_no_roles`, `Forbidden_iff_the_rule_is_unsatisfied`, `A_replay_by_a_caller_who_lost_the_role_is_forbidden_not_replayed`, `Roles_are_read_per_event_not_cached_on_the_draft`, `Start_requires_its_start_rule`
- `EventRuleStartupTests`: `An_undeclared_client_event_refuses_startup`, `Every_undeclared_event_is_listed_in_one_refusal`, `Two_edges_of_one_event_declaring_different_rules_refuse_to_build`, `A_rule_on_an_outcome_or_timer_trigger_refuses_to_build` (a guard, green today), `A_policy_rule_naming_an_unregistered_policy_refuses_startup`, `Anonymous_on_a_user_owned_machine_refuses_startup`, `An_undeclared_start_get_or_machineChanged_rule_refuses_startup`, `A_read_rule_looser_than_owner_only_refuses_startup`, `An_undeclared_discard_rule_refuses_startup`, `A_discard_rule_looser_than_owner_only_refuses_startup`, `A_gated_event_into_the_start_state_refuses_to_build`, `A_policy_gated_event_into_the_start_state_refuses_to_build`, `A_rule_naming_a_role_or_a_policy_is_gated_and_a_start_rule_counts`, `Exposing_a_raw_delegate_or_system_owned_machine_refuses_startup`, `An_unexposed_machine_with_the_generic_opt_in_off_needs_no_rules_and_has_no_surface`
- `MarkedMachineTests`: `A_marked_machine_is_served`, `An_unmarked_machine_is_not_served`, `A_marked_machine_the_host_registers_itself_is_served_as_a_scanned_one_is`, `A_marked_machine_the_host_registers_itself_is_checked_as_a_scanned_one_is`, `A_marked_machine_missing_a_rule_refuses_startup_and_says_it_is_marked`, `The_attribute_on_an_abstract_machine_refuses_the_host_rather_than_serving_its_subclass`, `The_attribute_on_a_class_that_is_no_machine_refuses_the_host`, `The_attribute_on_an_open_generic_machine_refuses_the_host`, `An_unmarked_subclass_of_a_marked_machine_is_not_served_and_refuses_the_host`, `A_marked_system_owned_machine_refuses_the_host_and_says_to_remove_the_attribute`, `A_base_machine_registered_beside_its_marked_subclass_refuses_the_host_naming_both`, `Two_unrelated_machine_types_with_one_id_refuse_the_host_naming_both`, `The_same_machine_type_registered_twice_starts_and_is_served_once`
- `PostgresChangeFeedTests`: `A_write_on_a_session_with_the_notices_off_sends_no_notice`, `A_polling_host_delivers_each_write_and_the_delete_without_notices`, `A_listening_host_whose_database_sends_no_notices_refuses_to_start`, `A_polling_host_whose_database_still_notifies_starts_and_warns`, `A_listening_host_whose_database_notifies_starts_and_hears_each_write`, `The_change_feed_refuses_a_value_it_does_not_name`, `A_polling_feed_on_one_machine_reads_only_that_machines_rows`
- `ChangeFeedSwitchTests`: `A_listening_host_refuses_a_trigger_that_fires_only_for_replication_sessions`, `A_listening_host_refuses_a_database_that_turns_the_notices_off_whatever_its_own_session_reads`, `The_switch_turns_the_notices_off_in_any_case`, `A_polling_host_whose_session_turns_the_notices_back_on_is_warned`, `A_polling_host_on_a_database_with_the_notices_off_starts_without_a_warning`
- `ChangeFeedStartupValidatorTests`: `A_listening_host_refuses_a_role_that_turns_the_notices_off`, `A_listening_host_whose_own_session_is_a_replica_refuses_to_start`, `A_listening_host_on_a_trigger_that_always_fires_starts`, `A_value_that_is_neither_on_nor_off_is_warned_of`
- `GenericSaveTimerTests`: `The_generic_save_is_refused_for_a_machine_with_a_timer`, `Autosave_cannot_leave_or_rewrite_a_timed_state`
- `MachineCensusTests`: `The_census_lists_every_event_and_read_with_its_rule_and_surfaces`, `The_census_lists_which_events_host_code_can_send`, `The_census_lists_discard_with_its_rule_and_surfaces`
- `GenericRulesTests`: `The_generic_load_applies_the_get_rule`, `A_blank_request_id_is_refused_on_every_event_path`, `A_blank_request_id_on_a_send_is_refused_before_the_effect_runs`
- `MachineTrainHookOutputTests`: `Every_machine_train_withholds_its_output_from_the_lifecycle_hooks`
- `RequestIdRingTests`: `Server_writes_never_evict_the_ring_and_a_seventeenth_client_request_evicts_the_oldest`
- `RequestIdRingSizeTests`: `A_ring_of_twenty_KiB_results_keeps_as_many_as_fit_in_the_stored_text`, `A_ring_written_with_every_character_escaped_still_replays`
- `RequestRetryAfterServerWriteTests`: `A_retried_send_after_a_timer_moved_the_instance_on_replays_its_recorded_result`
- `RequestIdScopeTests`: `A_retry_of_any_of_the_last_sixteen_requests_replays_its_recorded_result`, `A_server_write_never_evicts_a_client_request_from_the_ring`, `A_request_evicted_from_the_ring_is_answered_as_the_ADR_says`, `One_requestId_with_a_different_trigger_or_input_is_request_id_reused`, `Concurrent_first_attempts_of_one_request_fire_once`, `Concurrent_retries_of_a_recorded_request_replay_and_fire_nothing`
- `RevisionTests`: `A_new_draft_starts_at_revision_one_with_a_fresh_row_id`, `An_autosave_update_bumps_the_revision_by_one`, `An_advance_bumps_the_revision_by_one`, `Entering_an_invoking_state_bumps_once`, `Leaving_an_invoking_state_bumps_once`, `A_send_bumps_once_to_mark_and_once_to_record`, `A_system_advance_bumps_once`, `An_outcome_applied_by_the_hook_bumps_once`, `An_outcome_applied_by_the_reconciler_bumps_once`, `A_stranded_outcome_bumps_once`, `Setting_the_invoke_token_bumps_once`, `A_raw_UPDATE_from_host_code_bumps_the_revision`, `A_public_ISnapshotStore_write_bumps_once`, `A_compare_and_swap_write_reports_stored_plus_one`, `A_refusal_conflict_or_replay_never_bumps`, `Delete_then_recreate_under_one_id_is_a_new_row_id_and_restarts_at_one`, `Two_concurrent_writes_with_one_token_bump_once`, `A_revision_beyond_two_to_the_53_round_trips_exactly`, `The_canonical_wire_never_carries_revision_or_dueAt` (a guard, green today), `Existing_rows_get_revision_one_and_a_row_id_after_the_migration`, `The_EF_InMemory_provider_bumps_or_is_refused_as_the_ADR_says`, `A_custom_store_without_revision_still_works` (a guard, green today)
- `RevisionlessStore`: `Revision_equals_the_number_of_successful_writes`
- `RevisionConcurrencyTests`: `Twenty_concurrent_advances_leave_contiguous_revisions`
- `SnapshotChangeListenerTests`: `The_listener_reconnects_after_termination_and_its_subscribers_are_told_to_reload`, `A_write_during_the_listener_outage_reaches_the_subscriber_after_reconnect`, `One_listening_session_per_host_whatever_the_subscriber_count`, `A_notice_for_a_row_nobody_here_watches_reads_nothing`, `A_malformed_notice_is_metered_and_ignored`, `Notification_queue_usage_is_reported_for_alerting`, `Notification_queue_usage_is_read_every_interval_while_notices_keep_arriving`
- `SnapshotChangeNotifyTests`: `Save_notifies_once_with_its_row_id`, `Advance_notifies_once`, `Send_after_effect_notifies_once_for_the_mark_and_once_for_the_record`, `System_start_and_advance_notify_once`, `Start_and_discard_notify_once`, `Strand_notifies_once`, `Direct_ISnapshotStore_calls_notify_once`, `A_custom_store_writing_by_raw_sql_is_covered`, `Delete_and_DeleteHolding_notify_with_the_row_id`, `A_sweeper_bulk_delete_notifies_once_per_row_and_the_listener_keeps_up`, `A_rolled_back_write_sends_nothing`, `Two_writes_to_one_row_in_one_transaction_fold_into_one_notice`, `The_payload_carries_only_the_row_id_even_for_the_largest_context`, `Delete_then_recreate_under_one_id_is_a_new_row_id_incarnation`, `The_listener_channel_is_the_one_the_migration_notifies`, `The_trigger_migration_reruns_over_its_own_result`
- `SnapshotDraftExpiryTests`: `Load_no_longer_deletes_an_expired_draft`
- `SnapshotDraftServiceTimerTests`: `Advance_with_the_timer_trigger_is_refused_timer_bound`, `IMachineInstances_Advance_with_the_timer_trigger_is_refused`
- `SqliteSnapshotChangePollTests`: `Sqlite_polling_reports_each_new_revision_a_delete_and_a_recreate`, `Polling_a_reader_that_watches_some_rows_reports_only_those`, `Polling_stops_when_the_last_reader_leaves_and_starts_again_with_the_next`, `The_hub_lets_go_of_the_listener_when_its_last_watch_ends`, `A_row_watched_and_deleted_between_two_polls_ends_its_watch_with_gone`, `A_row_watched_and_deleted_between_two_polls_ends_its_watch_with_gone_while_every_row_is_watched`
- `StartTests`: `Start_returns_a_server_minted_id_at_revision_one`, `Start_validates_its_StartInput_record`, `Start_refuses_a_StartInput_reaching_a_server_written_field`, `Start_with_an_empty_whitespace_or_oversized_requestId_is_refused`, `Two_starts_with_one_requestId_and_one_input_return_one_instance`, `A_retried_start_with_a_different_input_is_request_id_reused`, `Two_users_with_one_requestId_get_different_instances`, `One_requestId_on_two_machines_gives_two_instances`, `Start_by_an_unauthenticated_or_whitespace_principal_writes_nothing`, `Start_into_an_invoking_initial_state_queues_the_run_in_its_transaction`, `Start_on_a_system_owned_machine_is_refused`, `Twenty_parallel_starts_with_one_requestId_create_one_row`, `Migration_adds_start_request_id_with_its_unique_index`
- `TimerDueAtTests`: `Start_into_a_timed_initial_state_sets_due_at`, `Advance_into_a_timed_state_sets_due_at`, `Advance_out_of_a_timed_state_clears_due_at`, `Self_entry_through_Extend_restarts_the_timer`, `A_rejected_event_writes_nothing`, `An_outcome_entering_a_timed_state_sets_due_at_by_hook_and_by_sweep`, `An_outcome_leaving_a_timed_invoking_state_clears_due_at`, `Send_after_effect_into_a_timed_state_sets_due_at`, `System_start_and_advance_set_due_at`, `A_timer_firing_into_another_timed_state_sets_its_due_at`, `A_strand_keeps_due_at_and_the_timer_still_leaves`, `Upsert_through_the_public_store_into_a_timed_state_sets_due_at`, `Duration_is_read_at_entry_not_at_fire`, `due_at_comes_from_the_database_clock`, `due_at_is_written_in_the_same_statement_as_the_state`, `due_at_is_utc_and_orders_correctly_on_sqlite`, `due_at_is_armed_from_when_it_is_written_not_from_when_its_transaction_began`
- `TimerFireConditionTests`: `A_fire_over_a_row_written_since_it_was_read_matches_nothing`, `A_fire_over_a_row_in_another_state_matches_nothing`
- `TimerSweepIsolationTests`: `A_database_that_keeps_failing_never_clears_a_timer`, `A_timer_due_while_an_effect_is_pending_waits_for_its_record`, `A_receipt_the_earlier_ledger_recorded_holds_the_timer_until_a_send_records_it`, `An_effect_past_its_lease_is_finished_before_its_timer_fires`, `A_receipt_for_other_content_does_not_hold_the_timer`, `Rows_with_a_pending_effect_do_not_starve_the_rows_behind_them`, `An_effect_sent_while_a_fire_is_being_written_never_runs`
- `TimerFailureClassificationTests`: `A_fire_failure_is_the_databases_by_its_SQLSTATE_class`, `A_timeout_is_the_databases`, `Any_other_failure_is_the_rows`
- `TriggerInputValidationPathTests`: `Every_path_validates_trigger_input_and_refuses_unknown_fields`, `Input_size_is_capped_before_validation_on_every_path`, `An_invalid_input_records_no_request_id_so_a_corrected_retry_fires`, `The_effect_runner_receipt_still_commits_on_an_effect_bound_trigger_with_WithInput` (a guard, green today)

In `Trax.Effect.StateMachine.Tests.Stress`:
- `RevisionStoreModelTests`: `Every_incarnation_commits_revisions_one_to_n_and_replays_to_its_final_row`

In `Trax.Effect.StateMachine.Tests`:
- `CheckoutConformanceTests`: `Advance_matches_the_shared_fixture`, which replays the bad-input fixtures with the rest
- `ConformanceTests`: `Advance_matches_the_shared_fixture`, which replays the bad-input fixtures with the rest
- `DifferentialCorpusReplayTests`: `Timer_triggers_replay_through_AdvanceTimer`
- `TwinSequenceParityTests`: `Random_event_sequences_with_timers_fired_at_random_points_agree`
- `EngineContractTests`: `Advance_refuses_an_outcome_trigger_in_both_engines` (a guard, green today), `Advance_refuses_a_timer_trigger_in_both_engines`, `The_corpus_replays_outcome_cases_through_the_outcome_entry_point` (a guard, green today), `An_outcome_output_that_fails_its_schema_is_refused_not_thrown`
- `IntrospectionTests`: `The_trigger_list_names_the_timer_with_kind_Timer_and_no_input`
- `IrExporterTests`: `Timers_export_as_their_own_trigger_kind`, `Timers_are_not_in_inputs`, `A_machine_without_timers_exports_no_timers_block` (a guard, green today)
- `EventWriteRuleTests`: `An_open_event_writing_a_field_a_gated_event_writes_refuses_to_build`, `An_event_with_no_rule_writing_a_field_a_policy_gated_event_writes_refuses_to_build`, `An_open_event_with_a_custom_reducer_and_a_gated_write_refuses_to_build`, `An_open_event_writing_only_other_fields_or_constants_builds`
- `RefusalPrecedenceTests`: `Invalid_input_on_a_trigger_with_no_edge_from_this_state_is_no_transition` (a guard, green today), `Invalid_input_is_reported_before_the_guard_runs`, `A_guard_never_sees_an_input_that_failed_its_schema`
- `SchemaFormatTests`: `A_renamed_enum_member_is_compared_exactly`, `A_flags_enum_has_no_fixed_JSON_type_and_refuses_to_build`, `A_property_reading_never_sets_is_not_an_input_field`, `A_dictionary_s_keys_carry_their_CLR_key_type`, `A_dictionary_keyed_by_a_type_no_schema_checks_refuses_to_build`, `Every_uri_the_format_accepts_is_one_System_Text_Json_reads`
- `TimerDeclarationTests`: `A_second_After_on_one_state_is_refused_at_build`, `A_self_loop_timer_is_refused_at_build`, `A_timer_only_cycle_is_refused_at_build`, `A_timer_into_an_effects_target_is_refused_at_build`, `A_timer_target_is_not_reserved` (a guard, green today), `The_timed_state_is_locked`, `A_user_edge_into_a_timer_target_is_allowed` (a guard, green today), `A_timer_into_an_invoking_state_on_a_user_owned_machine_refuses_startup`, `A_timer_into_an_invoking_state_on_a_system_owned_machine_is_allowed` (a guard, green today), `A_timer_leaving_an_invoking_state_is_allowed` (a guard, green today), `After_on_a_committed_state_leaves_it`, `A_zero_or_negative_fixed_duration_is_refused_at_build`, `A_fixed_duration_above_the_global_cap_is_refused_at_build`, `A_context_duration_with_min_above_max_or_min_not_positive_is_refused_at_build`, `A_duration_field_not_on_the_states_context_is_refused_at_build`, `A_duration_field_of_the_wrong_type_is_refused_at_build`, `A_delegate_duration_is_refused_export`, `A_duration_field_marked_TraxSensitive_is_refused_at_build`, `A_timer_edge_carries_the_union_of_the_rules_entering_its_source`, `Entering_a_timed_state_with_duration_null`, `Entering_a_timed_state_with_duration_zero`, `Entering_a_timed_state_with_duration_negative`, `Entering_a_timed_state_with_duration_below_min`, `Entering_a_timed_state_with_duration_above_max`, `Entering_a_timed_state_with_duration_max_value`, `Entering_a_timed_state_with_duration_in_range` (a guard, green today), `Entering_a_timed_state_with_duration_in_prose`, `Entering_a_timed_state_with_duration_as_a_number`, `Advance_never_applies_a_timer_trigger`, `AdvanceTimer_moves_only_from_its_state`
- `TriggerInputPropertyTests`: `Generated_inputs_get_the_same_verdict_in_both_engines`
- `TriggerInputValidationTests`: `The_valid_input_transitions` (a guard, green today), `A_missing_required_input_field_is_refused_as_invalid_input`, `A_number_sent_as_a_string_is_refused_as_invalid_input`, `An_unknown_input_field_is_refused_as_invalid_input`, `A_field_named_in_PascalCase_is_an_unknown_field_not_an_alias`, `A_non_object_input_is_refused_as_invalid_input`, `An_input_constraint_is_checked_against_the_input_not_the_context`, `A_trigger_without_WithInput_refuses_unknown_fields`, `An_int_field_refuses_a_fraction_and_accepts_an_integral_value`, `A_number_beyond_double_range_gets_one_verdict_in_both_engines`, `A_lone_surrogate_in_an_input_string_is_refused_not_an_internal_error`, `A_duplicate_key_in_the_input_is_refused_not_thrown`, `Two_edges_declaring_different_input_types_for_one_trigger_refuse_to_build`, `A_JsonPropertyName_on_an_input_record_is_honoured`, `A_recursive_input_type_refuses_to_build`, `A_nested_record_input_is_validated_as_strictly_as_its_CLR_type`, `A_deeply_nested_input_gets_a_verdict_without_overflowing`
- `TriggerMetadataTests`: `An_effect_bound_trigger_is_listed_as_effect_bound`, `A_raw_delegate_machine_lists_its_triggers_without_input_types`, `The_IR_separates_client_inputs_from_outcome_inputs`

In `Trax.Mediator.Tests.StateMachine.Integration`:
- `InvokesStartupRefusalTests`: `An_Operator_event_entering_an_invoking_state_may_invoke_an_Operator_train`, `An_Operator_event_entering_an_invoking_state_refuses_an_Admin_train`, `Every_event_entering_the_invoking_state_is_compared`, `An_any_of_role_event_admits_a_role_the_train_lacks_and_is_refused`, `A_policy_rule_against_a_role_train_cannot_be_proven_and_is_refused`, `A_train_with_two_role_sets_needs_an_event_satisfying_both`, `Start_entering_an_invoking_initial_state_is_compared_too`
- `SnapshotChangeOutcomeNotifyTests`: `Advance_into_invoke_notifies_once`

In `Trax.Samples.Recovery.Browser`:
- `TopicMapBrowserTests`: `TheWizard_RunsEndToEnd_WithAdHocDocumentsRefusedAndGenericMutationsOff`, `TheBuildOutcome_ArrivesByPush_WithNoPollingRequest`, `TheReviewTimer_ExpiresByPush`, `AViewerRolesOperatorOnlyEvent_IsRefusedOnTheServer`, `TheDashboardAndGraphQL_ShowTheSameStateRevisionAndRuns`, `ReloadingTheTab_ResumesTheServerMintedInstance`, `TwoTabs_SeeEachOthersTransitionsInRevisionOrder`, `ADeployWithANewIR_ShowsClientOutdated`, `KillingTheHostMidBuild_RecoversThroughTheStuckRunPath_AndThePageFollows`, `StartingANewMapFromBuilt_ResetsTheDraft_AndLeavesNoneBehind`

In `Trax.Samples.Recovery.E2E`:
- `TopicMapAuthorizationTests`: `BuildTopicMapTrain_has_its_Operator_roles_back_and_the_host_starts`
- `TopicMapDifferentialTests`: `TopicMapMachine_reproduces_its_committed_differential_corpus` (a guard, green today)
- `TopicMapMachineTests`: `AHandFiredTimer_IsRefused`, `StartingANewMapFromBuilt_ResetsTheDraft_AndLeavesNothingThatCountsTowardTheCap`
- `TriggerMetadataTests`: `The_topic_map_lists_each_trigger_with_its_kind_and_input_type`

In `Trax.Samples.StateMachine.E2E`:
- `GenericMutationsOptInTests`: `The_StateMachine_sample_runs_without_the_opt_in`

In `Trax.Samples.Tests.Chains`:
- `MachineAuthorizationCensusTests`: `Every_exposed_machines_events_and_reads_declare_a_rule`

In `Trax.Samples.Tests.Meta`:
- `NoTestFrameworkInSrcTests`: `OnlyTestProjects_ReferencePlaywright` (a guard, green today)
- `PlaywrightVersionTests`: `The_NuGet_and_npm_Playwright_versions_match` (a guard, green today)

In `Trax.Scheduler.Tests.Integration`:
- `SnapshotChangeOutcomeNotifyTests`: `An_outcome_applied_by_the_hook_notifies_once`, `An_outcome_applied_by_the_reconciler_on_a_second_host_notifies_once`
- `TimerDraftLifetimeTests`: `A_draft_past_its_TTL_with_a_pending_timer_survives_until_it_fires`, `A_pending_timer_never_keeps_a_draft_past_MaxDraftLifetime`, `The_TTL_sweeper_skips_a_future_due_at_and_counts_from_the_fire`
- `TimerFiringTests`: `A_due_timer_fires_once_across_two_hosts`, `A_timer_not_yet_due_does_not_fire`, `A_timer_only_machine_is_swept`, `Concurrent_sweeps_take_disjoint_SKIP_LOCKED_batches`, `A_host_not_registering_the_machine_leaves_the_row`, `The_hosted_reconciler_wakes_for_the_next_due_timer_before_its_interval`, `A_host_whose_clock_runs_ahead_does_not_fire_early`, `A_fire_that_cannot_be_applied_clears_due_at_and_is_metered`, `A_due_timer_waits_for_a_live_effect_claim`, `A_fire_produces_one_change_notice`, `A_timer_on_a_system_owned_instance_fires`, `A_client_event_leaving_first_wins_and_cancels_the_timer`, `A_fire_first_makes_the_client_event_a_no_transition`, `Extend_racing_the_fire_applies_exactly_one`, `An_outcome_and_a_timer_on_a_timed_invoking_state_apply_exactly_one`, `An_operator_cancel_and_a_timer_apply_exactly_one`, `Deleting_a_draft_with_a_due_timer_never_resurrects_it`, `Expiry_racing_a_fire_ends_deleted_or_expired_never_both`, `A_request_id_retry_after_the_timer_fired_replays_from_the_ring`, `A_timer_leaving_an_invoking_state_cancels_its_run_on_another_host`, `The_late_outcome_after_a_timer_left_is_a_no_transition`, `The_run_timeout_and_the_state_timer_apply_one`, `A_stranded_invoking_state_leaves_by_its_timer`
- `TimerVersionChangeTests`: `A_v2_without_the_timer_clears_a_stale_due_at_and_does_not_fire`, `A_changed_duration_keeps_the_due_at_set_at_entry`, `A_v1_host_does_not_fire_a_row_written_by_v2`, `A_migrated_snapshot_keeps_its_due_at_when_the_state_survives`
- `TimersModelTests`: `Generated_events_fires_and_races_keep_due_at_revision_and_row_id_in_step_with_the_model`

In `Trax.Api.StateMachine` (vitest; each was written `it.fails` and is now an ordinary test):
- `src/rules/interpreter.input.test.ts`: `validateInput refuses` a missing required field, a number sent as a string, an
  unknown field, a PascalCase field, a non-object input, a constraint broken by the input, unknown fields on a trigger
  without input, a fraction in an integer field, a number beyond double range, a lone surrogate, a nested record.
- `src/machine.inputTotality.test.ts`: `a grid of bad inputs never throws, and every verdict is invalid-input`.
- `src/machines/ingest/outcomes.test.ts`: `Advance_refuses_an_outcome_trigger_in_both_engines`.
- `src/machine.contract.test.ts`: `advance refuses outcome and timer triggers`.
- `src/differential.entrypoints.test.ts`: `The_corpus_replays_outcome_cases_through_the_outcome_entry_point`;
  `src/differential.timers.test.ts`: `differential corpus enumerates timer transitions through applyTimer`.
- `src/machines/reminder/timers.test.ts`: `can and available exclude timer triggers`,
  `timer(snapshot) reports trigger, target and declared duration`.
- `src/machines/turnstile/conformance.test.ts` and `src/machines/checkout/conformance.test.ts`: the bad-input
  advance fixtures (`coin-*.json` and `pay-*.json`: missing input, number as string, unknown field, `__proto__` key),
  each `invalid-input`, replayed by the C# `ConformanceTests` and `CheckoutConformanceTests` as well.
- `src/server/serverController.test.ts` and `src/server/useServerMachine.test.tsx`: the server-authoritative
  hook against `FakeMachineServer` (prediction and rollback, `clientResult`, per-instance serialisation and
  request ids, `server-outdated` retries, revision ordering, incarnation and reconnect reloads, `gone`,
  `client-outdated`, unmount, StrictMode, `dueAt` shown and never fired, `retry` under the first request id, the
  start request id kept across a reload).

In the Recovery sample's client: `TopicMapMachine reproduces its committed differential corpus` (green once the
golden is committed, as it is).

## Exemplars

**Enforced elsewhere:** the tests listed under *Tests*, in each folder's test projects and in
`Trax.Api.StateMachine`; `InteractionMatrixTests` in `Trax.Core.Tests.Meta`, which requires the `After` row of the
machine-features table and the *Machine API features* table to name a test for every cell; and the startup refusals
themselves (an undeclared event, an unexposed raw-delegate machine, the opt-in without a policy), which stop a host
that breaks the rule from starting.

Not covered: nothing can check that a host's own code forwards the right actor; a client is the only kind of actor
(`Trax.Effect.StateMachine.Persistence.Integration.ActorHierarchyTests`, in Trax.Effect), trusted host code reaches only system-owned instances through the named host API, and the
census lists what that code can send, but a host that drives a system-owned instance on a client's behalf has chosen
to trust it. The twin's prediction is checked against the server only where the differential corpus and
`clientResult` reach.

## Changelog

- **2026-10-13**: `MachineCensus`, `MachineCensusEntry`, `RequestIdRing`, `RecordedRequest`,
  `StateMachineInstruments` and the generic `StartSnapshot` and `DiscardSnapshot` contracts stay public: Trax.Api
  reads the request-id rule and the start contract, and the census and the instruments are documented for hosts that
  report on their machines or read their meters. The ring's byte count states its one known overcount.
- **2026-10-13**: The change feed's switch is database-wide. The 080 trigger reads `trax.snapshot_notify` in the
  session that writes, so a role setting, a connection string's `-c` option or a `SET LOCAL` silences some writers
  only, and a host on `Notify` never hears their writes; none of them is supported while any host listens. A host on
  `Notify` reads the database's own setting from the catalog rather than its session, and refuses to start when it, a
  role's setting or its own session turns the notices off, or when the trigger is not enabled for ordinary sessions
  (`tgenabled` other than `O` or `A`, so a trigger enabled for replicas only is refused); it warns when its session
  reads otherwise than the database. The trigger reads `'off'` in any case. An operator feed on one machine polls
  that machine's rows only. A snapshot write in the same transaction as another notice still takes the notify lock,
  so turning the notices off does not make every write free of it.
- **2026-10-13**: A node that serves no machine still registers, with an empty set of documents, at startup and on
  its timer. It returned early before, so a deploy that dropped its last `[TraxMachine]` never marked or collected
  the older documents, and their clients were answered `server-outdated` forever and never reloaded. Answering
  `client-outdated` for an id whose machine no node serves was the alternative; no node can know that without the
  stored marks, which is what registration keeps, so the registration runs instead. A failed registration on such a
  node is logged and retried rather than stopping the host, since it serves nothing that depends on it. A flood of
  made-up ids that pushes a real stale client's id out of the bounded set waiting for a read is accepted and
  documented under *Versioning and persisted documents*, rather than bounded per client or per machine.

- **2026-10-13**: Two registered machine types under one id refuse startup, naming both. Every machine is looked up by
  its id, and a subclass has its base's id, so serving a machine through a marked subclass while its base was also
  registered (a scan of the library's assembly, or the library's own registration) made every machine request answer
  `internal-error`. When one of the two derives from the other, the refusal says to register only the subclass; the
  derive-and-mark text says the base must not be registered and that a sealed machine cannot be served this way. The
  same type registered twice is one machine: the scan does not register again a type the host registered before it,
  and every lookup keeps each type once, so a machine registered twice is served once instead of refusing as two
  machines under one field. The refusal of the attribute on a type the scan cannot register now says that a closed
  generic machine the host registers by its closed type is served, which it already was.
- **2026-10-13**: A null due time always means the instance has no timer, or one the server cleared while it
  stayed in its state (a timer it gave up firing). An answer that cannot know the due time, a replay of a request
  the row has moved past since, says so with `dueAtKnown: false` beside `dueAt`, and the TypeScript client leaves
  `dueAt` out of it, so the controller keeps the one it holds or reads the instance once. The controller honours
  every null, a push in the same timed state included. A later event the server confirms supersedes one that failed
  before it: `retry()` resends nothing then, since resending would apply the failed event out of order.
- **2026-10-13**: A request id holding a control character (C0, DEL or C1) or an unpaired surrogate is refused as
  `invalid-request-id` on every path, before anything is read or run: Postgres cannot store a NUL or an unpaired
  surrogate in text, and a send ran its effect before the write that records it failed. The ring's byte count is
  of relaxed escaping, under which a character outside the Basic Multilingual Plane still costs 12 bytes, so it can
  overstate the column and never understates it.

- **2026-10-13**: An effect is pending on its instance's row (*Effects and server records*): its mark is set by a
  conditional write on the row, every other write is refused or waits while it is there, and the receipt, the move to
  the target and the end of the mark are one write. Past its lease the mark is taken over and the effect run again
  under its key, by the next send or by the reconciler. This replaces the claim ledger's role for machines and the
  rules that inferred from it whether a result had reached the row (the timer's hold, discard's settled claims, a
  reset's release). The 2026-10-12 entry rejected the claim as a column because batched fires would rewrite the
  whole batch around one bad row; fires are one row per transaction since, so that reason is gone. A row records
  whether the server put it in its state, and that record, not the kind of state, decides what discard refuses, what
  the draft sweep keeps and what the draft cap counts. An open event may not copy from its input a field any server
  write writes, an effect's receipt and an invoked train's outcome included, on a machine that declares rules. The
  tests named for this amendment are written first and marked `[Red]`. Built the same day: the mark carries a key
  minted when the row is marked, so a run again uses it and the next send is a new intent; a send is two writes, the
  mark and the record, so it bumps the revision and notifies twice; an effect neither runs from nor lands in a state
  that invokes a train; a timer's database failure is told from a row's by its SQLSTATE class. A host that substitutes
  its own `ISnapshotStore` cannot hold a mark and keeps the claim ledger, which now reads a receipt only under
  `receipt`.

- **2026-10-12**: Exposure matches trains. `[TraxMachine]` is the only switch, and it is read from every machine the
  host registers, by the `AddStateMachines` scan or as `IMachine` by hand, as train discovery reads every train in
  the service collection. `Expose<TMachine>()` is removed before its release, and there is no expose-all mode. A
  marked machine `AddTraxGraphQL` could not see, when it saw none, refuses startup naming it. Finding no machine is
  refused at startup rather than by `AddStateMachines`, since a host may register its machines after the call.
- **2026-10-12**: `discard` is also refused (`discard-not-allowed`) from the target of any event gated by `Requires`
  or `RequiresPolicy`, and while an effect on the instance is in flight or still recording its result. Its delete is
  conditional on the row id, revision and invoke token it checked, and releases only settled effect claims, in the
  delete's transaction. The draft sweep keeps an instance in a committed state, an effect's target, an invoked
  train's outcome target or a gated event's target, both past `DraftTtl` and past `MaxDraftLifetime`: that is the
  record of what the server did, and the host decides how long it stays. `MaxDraftLifetime` deletes every other user
  draft created longer ago, one waiting on an invoked run with its run cancelled first, and counts a row with no
  `created_at` from its last write. The sweep holds a draft's effect keys before it deletes, as a discard does, and
  skips a draft an effect is running on. A host that sets `DraftTtl` longer than `MaxDraftLifetime` refuses to start.
- **2026-10-12**: A due timer waits while an effect that runs from the instance's state has a claim: in flight,
  however long past its lease, or completed with a receipt for the content the instance holds and not yet applied,
  which the next send applies. A receipt for other content does not hold it. A fire that fails because the database
  failed (a provider error or a timeout) is backed off but not counted as an attempt; attempts are counted per host,
  in memory. A send whose request id the ring holds replays its recorded result wherever the instance is now, before
  the state checks, and carries the row's due time only while the row is at the recorded revision.
- **2026-10-12**: The ring's 128 KiB bound counts the UTF-8 JSON text written to `recent_requests`, written with only
  the escapes JSON requires: about six results of 20 KiB, one or two at the 64 KiB snapshot cap. A request evicted
  from it is handled as new.
- **2026-10-12**: A Postgres host may poll for changes instead of listening (`StateMachineOptions.ChangeFeed =
  Poll`), and migration 080 lets an operator turn the trigger's notices off with `trax.snapshot_notify = 'off'`,
  since every notifying transaction takes one cluster-wide lock held through its commit. A host left on `Notify`
  whose database sends no notices refuses to start; one on `Poll` whose database still notifies starts and warns.
  The listener reads `pg_notification_queue_usage()` every 30 seconds, busy or idle. The SQLite and InMemory poll
  reports a watched row that is missing once, so a row watched and deleted between two polls ends its watch with
  `gone`, also while a watch on every row is open.
- **2026-10-12**: SQLite and the InMemory provider poll only the rows a reader watches (every row while an operator
  feed watches every row), and only while a watch is open.
- **2026-10-12**: A marked or named machine is served as exactly that type: neither `[TraxMachine]` nor
  `Expose<TMachine>()` reaches a subclass, and a host registering an unmarked concrete subclass of a marked machine
  refuses to start. The attribute on an open generic machine or on a class that is no machine refuses to start, as on
  an abstract one. A startup refusal names its remedy by how the machine is served. A second `AddStateMachines` call
  refuses.
- **2026-10-12**: An id of a machine the node does not serve is answered from the store: `server-outdated` while the
  store holds it active, `client-outdated` once collected, and an unseen one `server-outdated` until a read that began
  after it was first asked for lacks it. `server-outdated` is HTTP 409, so neither answer is a server error.
  Registration marks the documents of a machine the registering node does not serve as `unexposed:` and collects them
  after the grace period; a node serving the machine clears the mark. A collected document is made active again only
  at startup or when no newer version is stored. The unseen ids waiting for a read are forgotten oldest first. A
  document id's machine is the segment up to the next underscore, and a machine id holds none.
- **2026-10-12**: An event open to any caller (no rule, `AnyAuthenticated` or `Anonymous`) may not copy from its input
  a field a gated event writes; it refuses to build, as the start does. The input schema is as strict as the CLR read:
  a `[Flags]` enum is refused, a dictionary's keys are checked as its key type (`keys`), a computed property is not an
  input field, a renamed enum member matches only as written (`enumExact`), and a `DateTimeOffset` has its own format.
- **2026-10-12**: The *Tests* list names the TypeScript files as they are now, since the red phase ended, and the
  tests added with these amendments.
- **2026-10-12**: A machine is served when its class is marked `[TraxMachine]`, as a train is by `[TraxMutation]`;
  `Expose<TMachine>()` stays for a machine the host does not own. The attribute is not inherited, and on anything but
  a concrete machine it refuses startup. The startup checks are unchanged. The samples mark their machines.
- **2026-10-12**: The Status no longer promises `[Experimental("TRAXEXP004")]`. The flag was never applied: the
  interaction matrix's *Machine API features* table and its `After` row were complete when the members were built,
  so there was nothing for it to wait on, and the id stays unused. The refusal precedence now states the order the
  draft service implements, which every surface shares: the request's shape (`invalid-request-id`, `too-large`) and
  the instance's existence (`not-found`) come before the caller's rule, so another user's id answers like a missing
  one, and the request-id ring comes before the engine refuses an unknown trigger. The generic opt-in serves six
  mutations, `startSnapshot` and `discardSnapshot` among them. Wording in the timer and scope sections changed with no
  change of decision.
- **2026-10-12**: The operator feed (`onMachineInstanceChanged`, `WatchMachineInstancesAsync`) sends a reload marker
  whenever the hub reloads, so a subscriber reads again what a listener outage may have hidden.
- **2026-10-12**: Exposing a machine requires persisted operations; startup refuses otherwise. Without an order of
  the versions, a `machineChanged` stream is kept rather than ended on an IR hash that differs from the node's.
- **2026-10-12**: A well-formed id of a machine a node does not serve answers `server-outdated` (it can only be a
  newer node's), not `client-outdated`; an unseen id is answered from the last read of the store, which is refreshed
  in the background, rather than by queueing on a read. Older documents are collected by a registration each running
  node repeats hourly, not only at startup.
- **2026-10-12**: `discard` declares a rule, `Discard(...)`, as `start`, `get` and `machineChanged` do: a machine a
  client can reach without one refuses startup, and owner-only is its floor. It is refused as `discard-not-allowed`
  from a committed state, an effect's target, an invoking state and an invoked train's outcome target, so the record
  of something the server did stays. The census lists it.
- **2026-10-12**: "Gated" covers policies as well as roles: a rule is gated when it is stricter than
  `AnyAuthenticated`. A policy-gated event may not enter the start state and its writes are server writes for the
  start's reach check, and the generic save counts a gated event or a gated `start` as server-side behaviour.
- **2026-10-12**: Host code on a user-owned instance acts as a client, holding the roles it passes (none through the
  `userKey` overloads), and is held to every rule; it has no trusted path, which the earlier text promised and nothing
  built. `Actor.Trusted` is removed, `Client` is the only kind of actor, and outside code cannot declare another.
  Trusted host code reaches system-owned instances only, through `IMachineInstances`.
- **2026-10-12**: The request ring is bounded in bytes as well as entries (`RequestIdRing.MaxBytes`, 128 KiB): sixteen
  results at the snapshot cap made about 1 MiB of JSON rewritten on every recorded advance and parsed on every read.
  Storing hashes and re-reading the row for a replay was the alternative; it cannot replay a result once a timer or an
  outcome has moved the row on, which is the retry the ring exists for. `MaxDraftLifetime` now defaults to 90 days, so
  a pending timer bounds a draft's life without the host opting in.
- **2026-10-12**: Due timers are fired one row per transaction rather than one batch per transaction, so a fire that
  throws (a run its timer queues that cannot be stored) rolls back its own row instead of every row in the batch, on
  every host, at every sweep. Such a row is counted on `TimerFireErrors`, backed off, and after five attempts cleared
  and counted on `TimerFireFailed`. A row waiting on a live effect claim, or backing off, is passed over without
  filling the batch and does not pace the loop at its one-second wake. The claim check alone left a window, a fire
  written but not committed while a send loaded the old state and claimed, so the effect runner now re-reads the row
  after its claim (on Postgres under a share lock that waits for the fire's transaction) and does not run the effect
  when the row has moved. Batched fires, with the claim as a column the sweep excludes, was the alternative; it moves
  the claim onto the snapshot row and leaves the poison-row problem.
- **2026-10-10**: The machines root types an instance's context as one object holding every field its states'
  records declare, each nullable, rather than a union per state. Because a typed answer cannot tell a null field from
  an absent one, `clientResult` is compared canonically with a null member equal to an absent one. An effect-bound
  event takes no input and no `clientResult`: the effect produces what its edge records.
- **2026-10-09**: The EF InMemory provider, which has no triggers, bumps the revision in the store.
- **2026-10-09**: Recorded.
