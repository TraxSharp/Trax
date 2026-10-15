---
title: "operations: execution queries"
description: "Reference for the operations queries over runs: executions, an execution's detail and children, junction steps, the run graph and decisions."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Queries
grand_parent: GraphQL API
nav_order: 4
source:
  - Trax.Api/src/Trax.Api.GraphQL/Queries/OperationsQueries.cs
---

# operations: execution queries

Fields of `OperationsQueries` that read train executions (metadata rows) and what a run recorded.

## executions

Returns a paginated list of train executions (metadata records), ordered by ID descending (newest first). Supports both offset-based and keyset cursor pagination.

```graphql
query {
  operations {
    executions(skip: 0, take: 10) {
      items {
        id
        externalId
        name
        trainState
        startTime
        endTime
        failureJunction
        failureReason
        manifestId
        cancellationRequested
        parentId
        currentlyRunningJunction
      }
      totalCount
      isEstimatedCount
      skip
      take
      nextCursor
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `Int!` | `0` | Number of records to skip (offset pagination). A negative value reads as `0` |
| `take` | `Int!` | `25` | Number of records to return, from 1 to 500. See [Page size](/docs/sdk-reference/graphql-api/queries/paged-result#page-size) |
| `trainState` | `TrainState` | `null` | Filter by state (`PENDING`, `IN_PROGRESS`, `COMPLETED`, `FAILED`, `CANCELLED`) |
| `trainName` | `String` | `null` | Exact-match filter on the train interface FullName |
| `startedAfter` | `DateTime` | `null` | Only executions with `startTime >= startedAfter` |
| `startedBefore` | `DateTime` | `null` | Only executions with `startTime <= startedBefore` |
| `order` | `SortOrder` | `NEWEST` | `NEWEST` (id descending) or `OLDEST` (id ascending). Both stay keyset-safe |
| `afterId` | `Long` | `null` | Keyset cursor. Returns records with `id < afterId` (or `id > afterId` when `order: OLDEST`). See [Pagination](/docs/sdk-reference/graphql-api/queries/paged-result#pagination) |
| `manifestId` | `Long` | `null` | Only executions of this manifest. The dashboard uses it for a manifest's execution history |
| `manifestGroupId` | `Long` | `null` | Only executions of any manifest in this group (resolved through `manifest.manifest_group_id`). The dashboard uses it for a group's recent executions |
| `hideAdminTrains` | `Boolean` | `false` | When `true`, excludes the framework's internal scheduler trains (matches `AdminTrains.FullNames` against `metadata.Name`, which stores the interface FullName). The dashboard sets this from its "Hide admin trains" toggle |
| `failureClass` | `FailureClass` | `null` | Only executions recorded with this [failure class](/docs/effect/failure-classification): `UNCLASSIFIED`, `TRANSIENT`, `CONFLICT`, or `PERMANENT`. Every run that did not fail records `UNCLASSIFIED`, so `failureClass: UNCLASSIFIED` on its own also matches every completed, pending, in-progress and cancelled run; combine it with `trainState: FAILED` for unclassified failures only |
| `externalId` | `String` | `null` | Only the execution with this external id, matched exactly. Served by `ix_metadata_external_id` |
| `parentId` | `Long` | `null` | Only executions started from inside this execution's run (its children), served by the partial index `ix_metadata_parent_id`. Unlike [`executionChildren`](#executionchildren) it combines with the other filters and pages in either `order` |
| `hostName` | `String` | `null` | Only executions run on the machine with this name (`hostName` on the summary), matched exactly. Served by `ix_metadata_host_name`. Combine it with `trainState: IN_PROGRESS` for what one host is running now |
| `failureReasonContains` | `String` | `null` | Only executions whose failure reason contains this text anywhere, ignoring case. `%`, `_` and `\` match themselves. Its count is capped; see below |
| `failureJunction` | `String` | `null` | Only executions that failed in the junction with this name (`failureJunction` on the summary), matched exactly. Served by the partial index `ix_metadata_failure_junction`, which holds only runs that failed |

When any filter is supplied the count is exact (`isEstimatedCount: false`), except under `failureReasonContains`; an unfiltered list may use the database's row estimate, on every page alike (see [Estimated counts](/docs/sdk-reference/graphql-api/queries/paged-result#estimated-counts)). With `failureReasonContains` the count stops at 10,000 matches: past that `totalCount` is `10000` and `isCountCapped` is `true`, so a client shows "10,000+" rather than waiting on a count of every failed run. A term matching 10,000 or fewer is counted exactly. `startedAfter`/`startedBefore` use the `ix_metadata_start_time_desc` index so they stay fast at scale. `manifestId` and `manifestGroupId` are served by the covering index `ix_metadata_manifest_state`, so a manifest's or group's history stays index-only even against millions of rows. `failureClass` is served by `ix_metadata_failure_class` on `(failure_class, id DESC)` (Postgres). It covers every row rather than only classified ones: the class arrives as a query parameter, and a generic plan cannot prove a parameter satisfies a partial index's predicate, so a partial index would go unused. Arbitrary-column sorting is deliberately not offered: it is incompatible with keyset pagination over millions of rows (it forces OFFSET scans or a full sort). Filter to narrow the set instead.

On Postgres `failureReasonContains` is served by a trigram index on `lower(failure_reason)`, `ix_metadata_failure_reason_trgm` (migration [066](/docs/reference/schema-migrations#failure-search-066)). A first page or a cursor page with a text filter is read in two steps: the 10,000 ids nearest where it starts, in order, which fills the page when the term is common there, and then the rest of the matches through the index, sorted. So a term found only in old runs does not make a newest-first page walk every newer run. At 3,000,000 runs, two in nine of them failed, a page with its count takes under 10 ms for a term that matches nothing, a few dozen runs or two runs in nine, and under 100 ms for a term found in 67,000 old runs only, alone or with `hostName`. A term shorter than three characters has no trigram to look up and reads the table. SQLite has no trigram index, so there both filters read the table.

**Returns**: `PagedResult<ExecutionSummary>`

### ExecutionSummary fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | `Long!` | Metadata ID |
| `externalId` | `String!` | External identifier |
| `name` | `String!` | Train type name |
| `trainState` | `TrainState!` | Current state: `PENDING`, `IN_PROGRESS`, `COMPLETED`, `FAILED` or `CANCELLED` on the wire |
| `startTime` | `DateTime!` | When execution began |
| `endTime` | `DateTime` | When execution finished (null if still running) |
| `failureJunction` | `String` | Name of the junction that failed (null if no failure) |
| `failureReason` | `String` | Exception message on failure |
| `manifestId` | `Long` | Associated manifest ID (null if not scheduler-initiated) |
| `cancellationRequested` | `Boolean!` | Whether cancellation was requested |
| `parentId` | `Long` | The execution that started this one from inside its run, or null |
| `currentlyRunningJunction` | `String` | The junction the execution is running now, while it is `IN_PROGRESS`; null otherwise |
| `failureClass` | `FailureClass!` | How the failure was classified: `UNCLASSIFIED`, `TRANSIENT`, `CONFLICT`, or `PERMANENT`. `UNCLASSIFIED` when the run did not fail, no [failure classifier](/docs/effect/failure-classification) is registered, or it did not recognise the failure. Later releases may add values to `FailureClass`; a client generated from an older schema should treat a value it does not know as `UNCLASSIFIED` rather than failing to read the response |

## executionDetail

Returns the full detail for a single execution, including the `input` / `output` payloads and
`stackTrace` that `executions` omits to keep list reads lean.

```graphql
query {
  operations {
    executionDetail(id: 100) {
      id
      externalId
      name
      trainState
      startTime
      endTime
      failureJunction
      failureReason
      failureException
      stackTrace
      input
      output
      manifestId
      cancellationRequested
      currentlyRunningJunction
      junctionStartedAt
      hostName
      hostEnvironment
      hostInstanceId
      hostLabels
      failureClass
      parentId
      scheduledTime
      executor
      replayDecisionsOf
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The execution's metadata ID |

**Returns**: `ExecutionDetail` (nullable; `null` when the ID does not exist).

`input` and `output` are the raw JSON payloads as stored (Postgres returns them in jsonb
canonical form). There is no separate junction table: junction context is the
`currentlyRunningJunction` (while `IN_PROGRESS`) and `failureJunction` (on failure) fields.
`childCount` is the number of sub-executions (metadata rows whose `parentId` is this
execution), for rendering a parent/child tree, and `parentId` is this execution's own parent. Nothing in Trax sets `parentId` at present, a
train dispatched from a junction included (see [Nested Trains](/docs/mediator#nested-trains)), so
it is `0` unless something outside Trax writes the column. `failureClass` is the same `FailureClass` enum as
on [`ExecutionSummary`](#executionsummary-fields). `scheduledTime` is when a scheduled run was due
(null for one that was not scheduled), `executor` is the project name of the process that ran it,
and `hostLabels` is the host's user-supplied labels as a JSON object. `replayDecisionsOf` (`Long`) is
the execution whose recorded decisions this run was queued to replay, by a requeue or a manifest's
retry that replays; it is null when the run replays nothing, a run queued to ask afresh included. The dashboard shows it as
**Replays Decisions Of**. `replayAbandoned` (`Boolean!`) is `true` when the run was queued to replay
`replayDecisionsOf` and asked its deciders afresh instead, because that replay could not be honoured;
`false` for a run that replayed or was never queued to. The decisions themselves are on
[`decisions`](#decisions). `resumeFrom` (`Long`) is the failed or cancelled execution this run
resumed from a [checkpoint](/docs/sdk-reference/train-methods/checkpoint), shown on the dashboard as
**Resumes**, and `resumeAt` (`String`) the step it resumed at, null when it resumed after the latest
checkpoint; both are null for a run that ran from the top.

`input` and `output` can hold credentials. They are on this single-row read and on no list; see
[Train inputs and the operations gate](/docs/sdk-reference/graphql-api/queries#train-inputs-and-the-operations-gate).

## executionChildren

Paginated child executions of a parent (metadata rows whose `parentId` matches the given id),
newest first. Keyset-paginated on id like the top-level `executions` list. Backed by the
partial index `ix_metadata_parent_id`, so it stays O(page size) even on the huge metadata table.

```graphql
query {
  operations {
    executionChildren(parentId: 100, take: 25) {
      items { id name trainState startTime endTime }
      totalCount
      nextCursor
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `parentId` | `Long!` | none | The parent execution's metadata id |
| `take` | `Int!` | `25` | Page size, from 1 to 500. See [Page size](/docs/sdk-reference/graphql-api/queries/paged-result#page-size) |
| `afterId` | `Long` | `null` | Keyset cursor (`id < afterId`) |

**Returns**: `PagedResult<ExecutionSummary>` (count is always exact).

## junctionRuns

The steps of one execution, in the order it reached them, as
[`AddJunctionEvents()`](/docs/sdk-reference/configuration/add-junction-events) recorded them: each
junction that ran, each question a routing step asked and the track it took. Empty for an
execution with none recorded, and for an id with no execution. Read through
`JunctionRunQueries.ForRun`, the query the dashboard's timeline reads too.

```graphql
query {
  operations {
    junctionRuns(metadataId: 100) {
      position
      kind
      name
      state
      startedAt
      durationMs
      failureClass
      questionKey
      answer
      confidence
      replayed
      answerWithheld
      nameWithheld
      trackPosition
      attempt
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `metadataId` | `Long!` | none | The execution's id. 0 or less is refused with `TRAX_INVALID_ARGUMENT` |
| `afterPosition` | `Int` | `null` | Only steps after this position, a keyset cursor for the next page |
| `take` | `Int!` | `500` | Page size, from 1 to 500 |

**Returns**: `[JunctionStep!]!`, the same type the [`onJunctionEvent`](/docs/sdk-reference/graphql-api/subscriptions#onjunctionevent)
subscription carries. A step carries no input, output or failure message, and an answer to a
question about a [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive#on-a-question-type)
type is never present. Once a routing step's answer is withheld, every later step of the run is
withheld too, whatever its kind: a junction, a question or a further route is named `(withheld)`,
with `nameWithheld` true, and a question or route has `questionKey`, `answer` and `confidence` null
and `answerWithheld` true. `trackPosition` is the position of the latest routing step before a step,
of any kind, and null before the first. Trax cannot tell where a track rejoins the chain, so every
step after a route counts as on its track. The recorded decider is not kept, so `decider` is always
null here.

A withheld step still records its kind, position, state and timing, and for a failed junction its
exception type and failure class. The run's own `failureJunction`, the train's failed event, and
a train started from a junction on the track are recorded as for any run. See
[Junction Events](/docs/effect/junction-events).

A junction whose end event was dropped stays `IN_PROGRESS` in these rows after its run has ended,
so read a step's `state` together with the execution's.

It answers to the operations gate, as [`executionDetail`](#executiondetail) does, so a caller
refused one is refused the other. The rows trail the live subscription by moments: a client following a running
execution subscribes first, then reads this, and keeps for each position whichever is further
along.

`nodeId` is the id of the declared node the step ran for, as [`runGraph`](#rungraph) names it: a junction carries its own node's id, a question the id of the `Decide` step that asked
it, a route the id of its routing step. It is null for a step recorded before node ids were, and
wherever the step's name is withheld, because the id names the track the step sits on.

`branchPath` is the path of the `Parallel` branch the step ran in, as in `Parallel#0/cocitation`,
or null outside any branch. Branches run side by side, so their steps interleave by position; group
them by `branchPath` to read one branch on its own. It is withheld wherever the step's name is, for
the same reason as `nodeId`: a branch inside a track names the track.

## runGraph

One execution drawn on its train's declared chain: each node with the steps the run recorded for it
and where it stands, the track each routing step took, each `Parallel` step's branches, and the
steps that match no node. It reads
the run's first 500 steps through `JunctionRunQueries.ForRun`, as [`junctionRuns`](#junctionruns)
does, and places them by `nodeId` through `RunGraphs.Match`, which the dashboard's run graph calls
too.

```graphql
query {
  operations {
    runGraph(metadataId: 100) {
      train
      hasGraph
      hash
      moreSteps
      canResume
      nodes {
        id
        kind
        state
        replayed
        trackTaken
        canResume
        checkpointed
        steps { position state failureClass }
        tracks { name taken nodes { id state } }
      }
      unmatchedSteps { position name nodeId state }
    }
  }
}
```

**Reading a graph at any depth.** `nodes` and each track's `nodes` nest, so a query that follows
them repeats `tracks { nodes { ... } }` once per level, and the server refuses a query that repeats
the same fields past a few levels (HotChocolate's field-cycle limit). A chain can nest `Parallel`
and routing steps deeper than that. `allNodes` lists every node of the graph at any depth in one
list instead: depth first, each node before the nodes on its tracks, and each track's nodes in
declared order. Each node carries `parentId` (the routing or `Parallel` step whose track it sits on,
null at the top level), `track` (that track's or branch's name) and `depth` (0 at the top level),
so a client rebuilds the tree by grouping the list on `parentId` and `track`, in list order. Select
only a node's tracks' own fields there (`name`, `taken`), not their `nodes`:

```graphql
query {
  operations {
    runGraph(metadataId: 100) {
      hasGraph
      allNodes {
        id kind state parentId track depth
        tracks { name taken }
      }
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `metadataId` | `Long!` | none | The execution's id. 0 or less is refused with `TRAX_INVALID_ARGUMENT` |

**Returns**: `RunGraph`, or null for an id with no execution. In C# its nodes are `RunGraphNode`s, their
tracks `RunGraphTrack`s, and a node's `state` a `RunNodeState` (all in `Trax.Api.DTOs`). A node's
`id` names the step and the routing step and track it sits in, as in `Switch<Lane>#0/Fast/Ship#0`,
and is what a recorded step's `nodeId` refers to. `kind` is a `ChainStepKind` (`CHAIN`, `I_CHAIN`,
`SHORT_CIRCUIT`, `EXTRACT`, `RESOLVE`, `SEED`, `DECIDE`, `SWITCH`, `GATE`, `SCALE`, `PARALLEL`,
`CHECKPOINT`), and `opaque` is true for a step whose junction is decided only at run time. A
`PARALLEL` step's `tracks` are its branches, one per `Branch` in declared order, and a step inside a
branch has the branch in its id, as in `Parallel#0/cocitation/ScoreCoCitation#0`. To read a train's
declared chain without a run, use [ChainGraph](/docs/sdk-reference/train-methods/chain-graph) in C#. A node's `state` is one of:

| State | Meaning |
|-------|---------|
| `COMPLETED`, `FAILED`, `CANCELLED`, `IN_PROGRESS` | From the steps recorded for it, the worst of them |
| `INTERRUPTED` | A step recorded for it started and never recorded its end, and the run has ended: the host stopped mid-junction, or the step's end event was dropped |
| `SKIPPED` | It sits on a track the run did not take |
| `NOT_REACHED` | Nothing was recorded for it |
| `NOT_RECORDED` | An `Extract`, `Seed` or `Resolve`, which records nothing when it runs, or a `CHECKPOINT` the run did not store |
| `WITHHELD` | It comes after a route whose answer is withheld, so the steps recorded there name no node |
| `RESTORED` | The run resumed from a checkpoint, and the node comes before the point it resumed at: the run skipped it, recorded nothing for it, and took what the steps before the checkpoint produced from the checkpoint |

A `CHECKPOINT` node records no step of its own: it is `COMPLETED` once the run stored it, unless a
withheld route came before it.

A junction whose end event was dropped keeps the `IN_PROGRESS` row its start wrote (see
[`junctionRuns`](#junctionruns)). The graph reads each step with the run's own state, so once the
execution has completed, failed or been cancelled, such a node is `INTERRUPTED` rather than
`IN_PROGRESS`: nothing is still running in a run that has ended. While the run runs, it stays
`IN_PROGRESS`.

`trackTaken` is the route's recorded answer, or the one track holding recorded steps when the
answer is missing.

A `PARALLEL` node runs every branch, so its `tracks` are its branches, none is `SKIPPED` for
another having run, and each branch's nodes stand as the run recorded them. Every branch is `taken`
once the step has started, and `trackTaken` is always null. The step records nothing of its own, so
its `state` is read from its branches: `FAILED` when any branch failed, `CANCELLED` when one was
stopped (a sibling's failure cancels the others, as does cancelling the run), `IN_PROGRESS` while
any branch has a step running or still to reach, `COMPLETED` once every branch has finished, and
`NOT_REACHED` when no branch has started. A branch has finished only when every node on it has, at
any depth along the tracks its routing steps took: a branch that ends in a routing step holds the
`Parallel` step open until the track the route took has run. Once the run has ended, a step that
would still be `IN_PROGRESS` is `INTERRUPTED`. A route whose answer is withheld inside one branch
withholds that branch's later nodes and every node after the join, but not the other branches,
which ran on their own. The graph is the chain as the host declares it now, so a step recorded before
node ids were, after a withheld route, or for a node the chain no longer declares, is listed in
`unmatchedSteps` rather than dropped. `hasGraph` is false when the host has no graph for the train;
`nodes` is then empty and every step is unmatched. `moreSteps` says the run recorded more than 500
steps, so a later node can show as not reached when it ran.

**Where a run can resume.** For a failed or cancelled execution, and for one that itself
resumed, the read also asks the [resume check](/docs/sdk-reference/train-methods/checkpoint#what-can-resume-where)
once, over the run's checkpoints and those of the runs it resumed, rather than once per node.
`canResume` on a node is true when
[`resumeExecution(id, from)`](/docs/sdk-reference/graphql-api/mutations/executions#resumeexecution) can resume
the run there, and `canResume` on the graph when it can resume after the latest checkpoint, with
`from` omitted. Both are false for a run that did not fail or was not cancelled, and for one a state
machine's step started. The mutation can still refuse a run the graph offers, for its saved input or
a resume already queued, with the reason. `checkpointed` is true on a node holding a checkpoint the
run can resume from. The graph says only that a checkpoint exists and where: what it holds, and the
tracks stored with it, are on no operator surface.

It answers to the operations gate, as `junctionRuns` does.

## decisions

One run's recorded decisions, in the order it made them: each question it asked a decider, the
answer it acted on (or refused), and the tracks routing steps took on it. These are the rows
[decision recording](/docs/core/decisions) writes to `trax.decision` and a
[requeue](/docs/sdk-reference/graphql-api/mutations/executions#requeueexecution) replays. It reads through
`IOperationsService.GetRecordedDecisionsAsync`, the read the dashboard's run page makes.

```graphql
query {
  operations {
    decisions(metadataId: 1234, take: 50) {
      items {
        id
        questionKey
        occurrence
        kind
        answer
        refused
        isRefused
        model
        decider
        replayed
        replayRefused
        routes
        stateHash
        decidedAt
        answerWithheld
        trackWithheld
      }
      nextCursor
    }
    executionDetail(id: 1234) { replayDecisionsOf replayAbandoned }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `metadataId` | `Long!` | none | The run's id. 0 or less is refused with `TRAX_INVALID_ARGUMENT` |
| `afterId` | `Long` | `null` | Only decisions recorded after this one: pass a page's `nextCursor` |
| `take` | `Int!` | `50` | Page size, from 1 to 500 |

**Returns**: `DecisionPage!` with `items: [DecisionRecord!]!`, `take: Int!` (after clamping) and
`nextCursor: Long` (the last decision's id, null on an empty page). A run that recorded none, and an
id with no run, return an empty page. At a million recorded decisions, a run with a few reads in
about a millisecond and a page of 500 deep into a 2,000-decision run in under 10 ms.

### DecisionRecord fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | `Long!` | The decision's id; decisions are recorded in id order |
| `metadataId` | `Long!` | The run that asked |
| `questionKey` | `String` | The question's key |
| `occurrence` | `Int!` | Which asking of the question this was in the run, from 0 |
| `kind` | `String` | `choice`, `score` or `yes_no` |
| `question` | `String` | The question as asked, with its instructions and criteria, as JSON |
| `answer` | `String` | The answer the run acted on, as JSON. For a refusal, the answer it would not act on, or null when the decider gave none |
| `refused` | `String` | Why the run would not act on the answer; null for an answer it acted on |
| `isRefused` | `Boolean!` | `true` when the run refused the answer and its step failed on it. Kept on a withheld row, where `refused` is null |
| `fingerprint` | `String` | Identifies the asking the answer was given to: the step, the state's type and the question's declaration |
| `model` | `String` | The model that answered, when the decider is a model |
| `decider` | `String` | The decider's type; null when the answer was replayed |
| `replayed` | `Boolean!` | `true` when the answer came from an earlier run rather than a decider |
| `replayRefused` | `String` | Why an earlier run's answer was not replayed, so the decider was asked afresh: the `replay_refused` member of `answer`. Null when there is none |
| `shadows` | `String` | What each shadow decider answered, as JSON |
| `routes` | `String` | The tracks routing steps took on the decision, as a JSON array of `{"track", "fallback_reason"}`; null when nothing routed on it |
| `stateHash` | `String` | The hash of the state the question was asked about (`k1:` keyed, `s1:` unkeyed); null when none was recorded |
| `decidedAt` | `DateTime!` | When the question was answered |
| `answerWithheld` | `Boolean!` | `true` when the question is about a [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive#on-a-question-type) type: `answer`, `refused`, `replayRefused`, `shadows` and `routes` are null, since each states or gives away the answer |
| `trackWithheld` | `Boolean!` | `true` once the run had taken a track on a withheld answer: every later decision keeps only `id`, `metadataId`, `occurrence`, `replayed`, `isRefused` and `decidedAt`, because which questions it asked would give the track away |

The stored rows keep every value either way, because a requeue replays from them; only this read
leaves them out, by the rule [`junctionRuns`](#junctionruns) follows. It answers to the operations
gate like every field here.

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries)
- [operations: execution mutations](/docs/sdk-reference/graphql-api/mutations/executions)
- [PagedResult](/docs/sdk-reference/graphql-api/queries/paged-result)
- [ChainGraph](/docs/sdk-reference/train-methods/chain-graph)
