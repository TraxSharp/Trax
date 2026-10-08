import { gql } from "@urql/core";

// Overview: health + the dashboard metrics block. metrics.dashboard is the heaviest read
// (several aggregations over the 7-day window), so pages poll it on an interval rather than
// on every interaction. See Trax.Docs/sdk-reference/graphql-api/queries.md.
export const OVERVIEW = gql`
  query Overview($range: MetricsRange!, $hideAdmin: Boolean!) {
    operations {
      health {
        status
        description
        queueDepth
        inProgress
        failedLastHour
        deadLetters
      }
      metrics {
        server {
          uptimeSeconds
          workingSetBytes
          gcHeapBytes
        }
        serverCpuPercent
        dashboard(range: $range, hideAdminTrains: $hideAdmin) {
          kpis {
            executionsToday
            successRate
            currentlyRunning
            unresolvedDeadLetters
          }
          executionsOverTime {
            timestamp
            completed
            failed
            cancelled
          }
          topFailures {
            trainName
            count
          }
          topAverageDurations {
            trainName
            averageMilliseconds
          }
          throughputSeries {
            trainName
            buckets {
              timestamp
              count
            }
          }
        }
      }
    }
  }
`;

// Registered trains. In-memory discovery, so it stays fast regardless of table size.
export const TRAINS = gql`
  query Trains($hideAdmin: Boolean!) {
    operations {
      trains(hideAdminTrains: $hideAdmin) {
        fullName
        serviceTypeName
        implementationTypeName
        inputTypeName
        outputTypeName
        lifetime
        isQuery
        isMutation
        graphQLName
        isBroadcastEnabled
        requiredRoles
        requiredPolicies
        inputSchema {
          name
          typeName
          isNullable
          enumValues
        }
        hasQueueSubjectKey
      }
    }
  }
`;

// Work queue entries, newest first. Optional status / trainName filters, a subject's queue
// (subjectKey, matched exactly) and a manifest's entries (manifestId).
export const WORK_QUEUE = gql`
  query WorkQueue(
    $take: Int!
    $afterId: Long
    $status: WorkQueueStatus
    $trainName: String
    $subjectKey: String
    $manifestId: Long
  ) {
    operations {
      workQueue {
        workQueues(
          take: $take
          afterId: $afterId
          status: $status
          trainName: $trainName
          subjectKey: $subjectKey
          manifestId: $manifestId
        ) {
          items {
            id
            externalId
            trainName
            status
            createdAt
            dispatchedAt
            priority
            dispatchAttempts
            manifestId
            subjectKey
            confirmedAt
            replayDecisionsOf
          }
          totalCount
          isEstimatedCount
          nextCursor
        }
      }
    }
  }
`;

// Dead letters, newest first. Optional status and manifest filters.
export const DEAD_LETTERS = gql`
  query DeadLetters($take: Int!, $afterId: Long, $status: DeadLetterStatus, $manifestId: Long) {
    operations {
      deadLetters {
        deadLetters(take: $take, afterId: $afterId, status: $status, manifestId: $manifestId) {
          items {
            id
            manifestId
            manifestName
            status
            deadLetteredAt
            reason
            retryCountAtDeadLetter
            resolvedAt
            resolutionNote
          }
          totalCount
          isEstimatedCount
          nextCursor
        }
      }
    }
  }
`;

// Logs, in id order (NEWEST first by default; a run's own log reads OLDEST first). afterId pages
// in the chosen order. messageContains / categoryContains match anywhere, ignoring case; a text
// filter counts at most 10,000 matches and then sets isCountCapped.
export const LOGS = gql`
  query Logs(
    $take: Int!
    $afterId: Long
    $metadataId: Long
    $minimumLevel: LogLevel
    $messageContains: String
    $categoryContains: String
    $order: SortOrder! = NEWEST
  ) {
    operations {
      logs {
        logs(
          take: $take
          afterId: $afterId
          metadataId: $metadataId
          minimumLevel: $minimumLevel
          messageContains: $messageContains
          categoryContains: $categoryContains
          order: $order
        ) {
          items {
            id
            metadataId
            eventId
            level
            category
            message
            exception
          }
          totalCount
          isEstimatedCount
          isCountCapped
          nextCursor
        }
      }
    }
  }
`;

// Manifests, newest first. hideAdminTrains leaves out the manifests of the scheduler's own trains.
export const MANIFESTS = gql`
  query Manifests(
    $take: Int!
    $afterId: Long
    $isEnabled: Boolean
    $scheduleType: ScheduleType
    $nameContains: String
    $manifestGroupId: Long
    $hideAdminTrains: Boolean! = false
  ) {
    operations {
      manifests(
        take: $take
        afterId: $afterId
        isEnabled: $isEnabled
        scheduleType: $scheduleType
        nameContains: $nameContains
        manifestGroupId: $manifestGroupId
        hideAdminTrains: $hideAdminTrains
      ) {
        items {
          id
          externalId
          name
          isEnabled
          scheduleType
          cronExpression
          intervalSeconds
          maxRetries
          lastSuccessfulRun
          manifestGroupId
          priority
          replayDecisionsOnRetry
        }
        totalCount
        isEstimatedCount
        nextCursor
      }
    }
  }
`;

// ── Single-item detail queries ────────────────────────────────────────────
export const EXECUTION_DETAIL = gql`
  query ExecutionDetail($id: Long!) {
    operations {
      executionDetail(id: $id) {
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
        childCount
        failureClass
        parentId
        scheduledTime
        executor
        hostLabels
        replayDecisionsOf
        replayAbandoned
      }
    }
  }
`;

// Child executions of a parent (parent/child tree on the detail page).
export const EXECUTION_CHILDREN = gql`
  query ExecutionChildren($parentId: Long!, $take: Int!, $afterId: Long) {
    operations {
      executionChildren(parentId: $parentId, take: $take, afterId: $afterId) {
        items {
          id
          name
          trainState
          startTime
          endTime
        }
        totalCount
        nextCursor
      }
    }
  }
`;

// workQueue.detail: the entry, its input (sensitive values masked by the API), and the entry it
// waits on for its subject. Null when no entry has the id.
export const WORK_QUEUE_DETAIL = gql`
  query WorkQueueDetail($id: Long!) {
    operations {
      workQueue {
        detail(id: $id) {
          id
          externalId
          trainName
          status
          createdAt
          dispatchedAt
          scheduledAt
          priority
          dispatchAttempts
          manifestId
          metadataId
          deadLetterId
          inputTypeName
          confirmedAt
          subjectKey
          replayDecisionsOf
          input
          subjectHeldBy
          subjectQueuedBehind
        }
      }
    }
  }
`;

// A requeue-all job, read on the node that started it; null once that node no longer knows it.
export const REQUEUE_ALL_JOB = gql`
  query RequeueAllJob($id: UUID!) {
    operations {
      deadLetters {
        requeueAllJob(id: $id) {
          id
          status
          awaitingAtStart
          startedAt
          finishedAt
          count
          message
        }
      }
    }
  }
`;

export const DEAD_LETTER_DETAIL = gql`
  query DeadLetterDetail($id: Long!) {
    operations {
      deadLetters {
        deadLetter(id: $id) {
          id
          manifestId
          manifestName
          status
          deadLetteredAt
          reason
          retryCountAtDeadLetter
          resolvedAt
          resolutionNote
          retryMetadataId
        }
      }
    }
  }
`;

// operations.manifestDetail: the manifest with its schedule details and its properties (the
// input every run gets), [TraxSensitive] values masked by the API. Null when no manifest has the id.
export const MANIFEST_DETAIL = gql`
  query ManifestDetail($id: Long!) {
    operations {
      manifestDetail(id: $id) {
        id
        externalId
        name
        isEnabled
        scheduleType
        cronExpression
        intervalSeconds
        maxRetries
        timeoutSeconds
        lastSuccessfulRun
        manifestGroupId
        manifestGroupName
        dependsOnManifestId
        priority
        propertyTypeName
        properties
        misfirePolicy
        misfireThresholdSeconds
        scheduledAt
        nextScheduledRun
        varianceSeconds
        replayDecisionsOnRetry
      }
    }
  }
`;

// Manifest groups, newest first.
export const MANIFEST_GROUPS = gql`
  query ManifestGroups($take: Int!, $afterId: Long, $nameContains: String) {
    operations {
      manifestGroups {
        groups(take: $take, afterId: $afterId, nameContains: $nameContains) {
          items {
            id
            name
            maxActiveJobs
            priority
            isEnabled
            createdAt
            updatedAt
          }
          totalCount
          isEstimatedCount
          nextCursor
        }
      }
    }
  }
`;

// A single group plus its 1-hop cross-group dependency neighborhood (for the DAG).
export const MANIFEST_GROUP_DETAIL = gql`
  query ManifestGroupDetail($id: Long!) {
    operations {
      manifestGroups {
        group(id: $id) {
          id
          name
          maxActiveJobs
          priority
          isEnabled
        }
        graph(groupId: $id) {
          nodes {
            id
            name
            isHighlighted
          }
          edges {
            fromId
            toId
          }
        }
      }
    }
  }
`;

// The whole cross-group dependency graph (every group + cross-group edges), for the global
// dependency visual on the manifest-groups page.
export const GROUP_DEPENDENCY_GRAPH = gql`
  query GroupDependencyGraph {
    operations {
      manifestGroups {
        dependencyGraph {
          nodes {
            id
            name
            isHighlighted
          }
          edges {
            fromId
            toId
          }
        }
      }
    }
  }
`;

// Live scheduler runtime settings (the editable subset).
export const SCHEDULER_CONFIG = gql`
  query SchedulerConfig {
    operations {
      config {
        scheduler {
          manifestManagerEnabled
          jobDispatcherEnabled
          manifestManagerPollingInterval
          jobDispatcherPollingInterval
          maxActiveJobs
          defaultMaxRetries
          defaultRetryDelay
          retryBackoffMultiplier
          maxRetryDelay
          defaultJobTimeout
          stalePendingTimeout
          recoverStuckJobsOnStartup
          deadLetterRetentionPeriod
          autoPurgeDeadLetters
          localWorkerCount
          metadataCleanupInterval
          metadataCleanupRetention
          failureCountWindow
        }
      }
    }
  }
`;

// Executions, newest first. Paginate with keyset cursors: pass the previous page's
// nextCursor as afterId. Never use a deep `skip` (it scans every skipped row).
export const EXECUTIONS = gql`
  query Executions(
    $take: Int!
    $afterId: Long
    $trainState: TrainState
    $trainName: String
    $startedAfter: DateTime
    $startedBefore: DateTime
    $order: SortOrder
    $manifestId: Long
    $manifestGroupId: Long
    $hideAdminTrains: Boolean
    $failureClass: FailureClass
    $externalId: String
    $parentId: Long
    $hostName: String
  ) {
    operations {
      executions(
        take: $take
        afterId: $afterId
        trainState: $trainState
        trainName: $trainName
        startedAfter: $startedAfter
        startedBefore: $startedBefore
        order: $order
        manifestId: $manifestId
        manifestGroupId: $manifestGroupId
        hideAdminTrains: $hideAdminTrains
        failureClass: $failureClass
        externalId: $externalId
        parentId: $parentId
        hostName: $hostName
      ) {
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
          hostName
          failureClass
          cancellationRequested
          parentId
          currentlyRunningJunction
        }
        totalCount
        isEstimatedCount
        nextCursor
      }
    }
  }
`;

// Canonical FullNames of the internal/administrative scheduler trains. The executions grid filters
// these server-side via hideAdminTrains; the client uses this list to filter the live subscription
// feed (which streams every train on an admin host) by the same rule.
export const ADMIN_TRAIN_NAMES = gql`
  query AdminTrainNames {
    operations {
      adminTrainNames
    }
  }
`;

// Processes that have run trains, rolled up by host instance. Backs the cluster page.
export const HOSTS = gql`
  query Hosts {
    operations {
      hosts {
        instanceId
        name
        environment
        lastSeen
        totalExecutions
        currentlyRunning
      }
    }
  }
`;

// Execution roll-up for a single train, keyed by interface FullName. Backs the train detail page.
export const TRAIN_STATS = gql`
  query TrainStats($trainName: String!) {
    operations {
      trainStats(trainName: $trainName) {
        trainName
        total
        completed
        failed
        inProgress
        pending
        cancelled
        lastRun
        lastSuccessfulRun
        averageMilliseconds
      }
    }
  }
`;

// Per-manifest run stats (counts by state + last run), for the manifest detail summary cards.
export const MANIFEST_STATS = gql`
  query ManifestStats($manifestId: Long!) {
    operations {
      manifestStats(manifestId: $manifestId) {
        manifestId
        total
        completed
        failed
        inProgress
        pending
        cancelled
        lastRun
        lastSuccessfulRun
      }
    }
  }
`;

// Schedule exclusion windows for a manifest (typed per kind), for the manifest detail page.
export const MANIFEST_EXCLUSIONS = gql`
  query ManifestExclusions($manifestId: Long!) {
    operations {
      manifestExclusions(manifestId: $manifestId) {
        type
        daysOfWeek
        dates
        startDate
        endDate
        startTime
        endTime
      }
    }
  }
`;

// Batched per-group stats (manifest count + execution counts + last run) for the visible page
// of the manifest groups list.
export const MANIFEST_GROUP_STATS = gql`
  query ManifestGroupStats($groupIds: [Long!]!) {
    operations {
      manifestGroups {
        stats(groupIds: $groupIds) {
          groupId
          manifestCount
          totalExecutions
          completed
          failed
          inProgress
          lastRun
        }
      }
    }
  }
`;

// Registered effects and their runtime state in the API process. The configuration is redacted by
// the API: a [TraxSensitive] value never leaves the server.
export const EFFECTS = gql`
  query Effects {
    operations {
      effects {
        name
        fullName
        enabled
        toggleable
        isConfigurable
        configurationTypeName
        configuration
        fields {
          name
          typeName
          kind
          nullable
          enumValues
          sensitive
          hasValue
          value
          hint
        }
      }
    }
  }
`;

// One run's recorded decisions, in the order it made them. afterId is the previous page's
// nextCursor. Withheld answers and tracks arrive already blanked by the API.
export const DECISIONS = gql`
  query Decisions($metadataId: Long!, $afterId: Long, $take: Int!) {
    operations {
      decisions(metadataId: $metadataId, afterId: $afterId, take: $take) {
        items {
          id
          metadataId
          questionKey
          occurrence
          kind
          question
          answer
          refused
          isRefused
          fingerprint
          model
          decider
          replayed
          shadows
          routes
          stateHash
          decidedAt
          answerWithheld
          trackWithheld
          replayRefused
        }
        take
        nextCursor
      }
    }
  }
`;

// A run's steps, as AddJunctionEvents recorded them, ordered by position. afterPosition is a
// keyset cursor: only steps after it. The API clamps take to 500, the page the timeline caps at.
export const JUNCTION_RUNS = gql`
  query JunctionRuns($metadataId: Long!, $afterPosition: Int, $take: Int!) {
    operations {
      junctionRuns(metadataId: $metadataId, afterPosition: $afterPosition, take: $take) {
        position
        kind
        name
        state
        startedAt
        endedAt
        durationMs
        failureClass
        failureException
        questionKey
        answer
        confidence
        replayed
        decider
        answerWithheld
        attempt
        nameWithheld
        trackPosition
      }
    }
  }
`;

// The host's environment name (ASPNETCORE_ENVIRONMENT), for the header badge.
export const ENVIRONMENT_NAME = gql`
  query EnvironmentName {
    operations {
      config {
        environmentName
      }
    }
  }
`;

// The version of Trax serving the API, for the footer.
export const SERVER_VERSION = gql`
  query ServerVersion {
    operations {
      config {
        version
      }
    }
  }
`;

// The level each category configured under Logging:LogLevel filters at now, with the configured
// value and whether a runtime change overrides it.
export const LOG_LEVELS = gql`
  query LogLevels {
    operations {
      config {
        logLevels {
          category
          level
          configuredLevel
          overridden
        }
      }
    }
  }
`;

// ── Persisted operations ───────────────────────────────────────────────────
// The operations.persistedOperations namespace exists only when the host calls
// UsePersistedOperations(...). Selecting only __typename on it reads nothing, so this is a cheap
// probe: it resolves when the namespace is there and fails validation when it is not.
export const PERSISTED_OPERATIONS_AVAILABLE = gql`
  query PersistedOperationsAvailable {
    operations {
      persistedOperations {
        __typename
      }
    }
  }
`;

// Persisted operations, most recently updated first. Offset-paged (the API has no keyset cursor
// here); the filter's tenantKey is null for every tenant and "" for the default tenant.
export const PERSISTED_OPERATIONS = gql`
  query PersistedOperations($filter: PersistedOperationFilterInput, $skip: Int!, $take: Int!) {
    operations {
      persistedOperations {
        persistedOperations(filter: $filter, skip: $skip, take: $take) {
          items {
            id
            tenantKey
            operationName
            version
            isActive
            description
            updatedAt
          }
          totalCount
        }
      }
    }
  }
`;

// One persisted operation (null when missing) and its audit history, most recent first.
export const PERSISTED_OPERATION_DETAIL = gql`
  query PersistedOperationDetail($id: String!, $tenantKey: String, $historyTake: Int!) {
    operations {
      persistedOperations {
        persistedOperation(id: $id, tenantKey: $tenantKey) {
          id
          tenantKey
          operationName
          version
          document
          shapeFingerprint
          isActive
          deprecationReason
          description
          createdAt
          updatedAt
        }
        persistedOperationHistory(id: $id, tenantKey: $tenantKey, take: $historyTake) {
          historyId
          id
          tenantKey
          shapeFingerprint
          changeType
          changedAt
          changedReason
        }
      }
    }
  }
`;

// ── Run graph ──────────────────────────────────────────────────────────────
// One node of the run graph: never a checkpoint's state, only that one is stored (checkpointed)
// and whether resumeExecution can resume there (canResume).
const RUN_GRAPH_NODE = `
  id
  kind
  opaque
  replayed
  checkpointed
  canResume
  state
  steps {
    state
    failureClass
    failureException
  }
`;

const RUN_GRAPH_TRACK = `
  name
  description
  isFallback
  taken
`;

// One run drawn on its train's declared chain (operations.runGraph), the read the Blazor run page's
// run graph makes. GraphQL has no recursion, so tracks are read four levels deep.
export const RUN_GRAPH = gql`
  query RunGraph($metadataId: Long!) {
    operations {
      runGraph(metadataId: $metadataId) {
        metadataId
        hasGraph
        moreSteps
        canResume
        unmatchedSteps {
          position
          name
          nameWithheld
          state
        }
        nodes {
          ${RUN_GRAPH_NODE}
          tracks {
            ${RUN_GRAPH_TRACK}
            nodes {
              ${RUN_GRAPH_NODE}
              tracks {
                ${RUN_GRAPH_TRACK}
                nodes {
                  ${RUN_GRAPH_NODE}
                  tracks {
                    ${RUN_GRAPH_TRACK}
                    nodes {
                      ${RUN_GRAPH_NODE}
                    }
                  }
                }
              }
            }
          }
        }
      }
    }
  }
`;

// ── State machines ─────────────────────────────────────────────────────────
// Operators see instances read-only and never their context: none of these selects it.

// How many instances each machine has in each state, by owner kind. Exact, where the list's total
// stops at 10,000.
export const MACHINE_INSTANCE_COUNTS = gql`
  query MachineInstanceCounts {
    operations {
      machineInstanceCounts {
        machine
        state
        ownerKind
        count
      }
    }
  }
`;

// Instances newest first by when each was last written. Offset-paged: the order is by a time that
// moves, so the API has no cursor here.
export const MACHINE_INSTANCES = gql`
  query MachineInstances($machine: String, $state: String, $ownerKind: SnapshotOwnerKind, $skip: Int!, $take: Int!) {
    operations {
      machineInstances(machine: $machine, state: $state, ownerKind: $ownerKind, skip: $skip, take: $take) {
        items {
          machine
          ownerKind
          id
          rowId
          state
          version
          createdAt
          updatedAt
          hasLiveInvokedRun
        }
        totalCount
        isCountCapped
      }
    }
  }
`;

// One instance with the runs it invoked (newest first, at most 50). A user's draft is named by its
// rowId too, because several users can each hold a draft under one id.
export const MACHINE_INSTANCE = gql`
  query MachineInstance($machine: String!, $ownerKind: SnapshotOwnerKind!, $id: UUID!, $rowId: Long) {
    operations {
      machineInstance(machine: $machine, ownerKind: $ownerKind, id: $id, rowId: $rowId) {
        machine
        ownerKind
        id
        rowId
        state
        version
        createdAt
        updatedAt
        hasLiveInvokedRun
        queuedInvokedRunEntryId
        isInvokedRunsCapped
        invokedRuns {
          id
          externalId
          name
          trainState
          startTime
          endTime
          failureClass
          cancellationRequested
          isLive
        }
      }
    }
  }
`;
