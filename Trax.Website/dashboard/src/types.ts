// TypeScript mirrors of the Trax.Api DTOs and enums as they appear over GraphQL.
// HotChocolate serializes enum members in SCREAMING_SNAKE_CASE.

export type TrainState =
  | "PENDING"
  | "IN_PROGRESS"
  | "COMPLETED"
  | "FAILED"
  | "CANCELLED";

export type MetricsRange = "LAST60_MINUTES" | "LAST24_HOURS";

// Domains carried by the coalesced onDataChanged signal.
export type ChangeDomain =
  | "WORK_QUEUE"
  | "DEAD_LETTER"
  | "MANIFEST"
  | "MANIFEST_GROUP"
  | "SCHEDULER_CONFIG"
  | "EXECUTION";

export interface HealthStatus {
  status: string;
  description: string;
  queueDepth: number;
  inProgress: number;
  failedLastHour: number;
  deadLetters: number;
}

export interface DashboardKpis {
  executionsToday: number;
  successRate: number;
  currentlyRunning: number;
  unresolvedDeadLetters: number;
}

export interface ExecutionsBucket {
  timestamp: string;
  completed: number;
  failed: number;
  cancelled: number;
}

export interface TrainFailureCount {
  trainName: string;
  count: number;
}

export interface TrainAverageDuration {
  trainName: string;
  averageMilliseconds: number;
}

export interface ThroughputBucket {
  timestamp: string;
  count: number;
}

export interface ThroughputSeries {
  trainName: string;
  buckets: ThroughputBucket[];
}

export interface DashboardMetrics {
  kpis: DashboardKpis;
  executionsOverTime: ExecutionsBucket[];
  topFailures: TrainFailureCount[];
  topAverageDurations: TrainAverageDuration[];
  throughputSeries: ThroughputSeries[];
}

export interface ServerMetrics {
  processStartTimeUtc: string;
  uptimeSeconds: number;
  workingSetBytes: number;
  gcHeapBytes: number;
}

export interface InputPropertySchema {
  name: string;
  typeName: string;
  isNullable: boolean;
  // The member names an enum property accepts (as the input JSON spells them); null otherwise.
  enumValues?: string[] | null;
}

export interface TrainInfo {
  // The interface FullName: what queueTrain, trainStats and executions(trainName:) accept.
  fullName: string;
  // A friendly display name; not accepted by any field that takes a train.
  serviceTypeName: string;
  implementationTypeName: string;
  inputTypeName: string;
  outputTypeName: string;
  lifetime: string;
  isQuery: boolean;
  isMutation: boolean;
  graphQLName: string | null;
  isBroadcastEnabled: boolean;
  requiredRoles: string[];
  requiredPolicies: string[];
  inputSchema: InputPropertySchema[];
  // Whether the train overrides QueueSubjectKey, so its queued runs for one subject run one at a
  // time. A run started with runTrain bypasses that. Optional because older fixtures predate it.
  hasQueueSubjectKey?: boolean;
}

// How a failure was classified. UNCLASSIFIED for a run that did not fail.
export type FailureClass = "UNCLASSIFIED" | "TRANSIENT" | "CONFLICT" | "PERMANENT";

export interface ExecutionSummary {
  id: number;
  externalId: string;
  name: string;
  trainState: TrainState;
  startTime: string;
  endTime: string | null;
  failureJunction: string | null;
  failureReason: string | null;
  manifestId: number | null;
  hostName: string | null;
  // Optional because older fixtures and the live-feed rows predate it.
  failureClass?: FailureClass;
  // Set once someone has asked the run to cancel; it stops at its next junction.
  cancellationRequested?: boolean;
  // The run that started this one from inside its own run, or null.
  parentId?: number | null;
  // The junction an IN_PROGRESS run is running now; null otherwise.
  currentlyRunningJunction?: string | null;
}

export interface ExecutionDetail extends ExecutionSummary {
  failureClass?: FailureClass;
  parentId?: number | null;
  scheduledTime?: string | null;
  executor?: string | null;
  // The host's labels as a JSON object string.
  hostLabels?: string | null;
  // The run whose recorded decisions this run replays, when it is a replaying retry.
  replayDecisionsOf?: number | null;
  // True when the run was queued to replay replayDecisionsOf and asked its deciders afresh
  // instead, because that replay could not be honoured.
  replayAbandoned?: boolean;
  failureException: string | null;
  stackTrace: string | null;
  input: string | null;
  output: string | null;
  cancellationRequested: boolean;
  currentlyRunningJunction: string | null;
  junctionStartedAt: string | null;
  hostEnvironment: string | null;
  hostInstanceId: string | null;
  childCount: number;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  isEstimatedCount: boolean;
  skip: number;
  take: number;
  nextCursor: number | null;
  // True only when a text-filtered log count stopped at its cap (10,000): show "10,000+".
  isCountCapped?: boolean;
}

export type WorkQueueStatus = "QUEUED" | "DISPATCHED" | "CANCELLED";

export type DeadLetterStatus =
  | "AWAITING_INTERVENTION"
  | "RETRIED"
  | "ACKNOWLEDGED";

export type ScheduleType =
  | "NONE"
  | "CRON"
  | "INTERVAL"
  | "ON_DEMAND"
  | "DEPENDENT"
  | "DORMANT_DEPENDENT"
  | "ONCE";

export type LogLevel =
  | "TRACE"
  | "DEBUG"
  | "INFORMATION"
  | "WARNING"
  | "ERROR"
  | "CRITICAL"
  | "NONE";

export interface WorkQueueSummary {
  id: number;
  externalId: string;
  trainName: string;
  status: WorkQueueStatus;
  createdAt: string;
  dispatchedAt: string | null;
  scheduledAt: string | null;
  priority: number;
  dispatchAttempts: number;
  manifestId: number | null;
  metadataId: number | null;
  deadLetterId: number | null;
  inputTypeName: string | null;
  // Null while a two-phase enqueue has staged the entry and its OnQueue hook has not returned.
  confirmedAt?: string | null;
  subjectKey?: string | null;
  // The run whose recorded decisions the run this entry starts will replay; null when it asks
  // its questions afresh.
  replayDecisionsOf?: number | null;
}

// workQueue.detail: the entry plus its masked input and what it waits on for its subject.
export interface WorkQueueDetail extends WorkQueueSummary {
  input: string | null;
  // The running entry that holds this entry's subject.
  subjectHeldBy: number | null;
  // The queued entry ahead of this one for the same subject.
  subjectQueuedBehind: number | null;
}

export interface DeadLetterSummary {
  id: number;
  manifestId: number;
  manifestName: string;
  status: DeadLetterStatus;
  deadLetteredAt: string;
  reason: string;
  retryCountAtDeadLetter: number;
  resolvedAt: string | null;
  resolutionNote: string | null;
  retryMetadataId: number | null;
}

export interface LogEntry {
  id: number;
  metadataId: number;
  eventId: number;
  level: LogLevel;
  category: string;
  message: string;
  exception: string | null;
  stackTrace: string | null;
}

export interface ManifestSummary {
  id: number;
  externalId: string;
  name: string;
  isEnabled: boolean;
  scheduleType: ScheduleType;
  cronExpression: string | null;
  intervalSeconds: number | null;
  maxRetries: number;
  timeoutSeconds: number | null;
  lastSuccessfulRun: string | null;
  manifestGroupId: number;
  dependsOnManifestId: number | null;
  priority: number;
  manifestGroupName?: string | null;
  // On: a retry of a failed run replays the decisions that run recorded. Off: it asks afresh.
  replayDecisionsOnRetry?: boolean;
}

export type MisfirePolicy = "FIRE_ONCE_NOW" | "DO_NOTHING";

// operations.manifestDetail: the manifest plus its schedule and (masked) properties.
export interface ManifestDetail extends ManifestSummary {
  manifestGroupName: string | null;
  propertyTypeName: string | null;
  // The manifest's input as JSON, with [TraxSensitive] values already masked by the API.
  properties: string | null;
  misfirePolicy: MisfirePolicy;
  misfireThresholdSeconds: number | null;
  scheduledAt: string | null;
  nextScheduledRun: string | null;
  varianceSeconds: number | null;
  replayDecisionsOnRetry: boolean;
}

export interface ManifestGroupSummary {
  id: number;
  name: string;
  maxActiveJobs: number | null;
  priority: number;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface DependencyGraphNode {
  id: number;
  name: string;
  isHighlighted: boolean;
}

export interface DependencyGraphEdge {
  fromId: number;
  toId: number;
}

export interface ManifestGroupDependencyGraph {
  nodes: DependencyGraphNode[];
  edges: DependencyGraphEdge[];
}

// A process (host instance) that has executed trains. Backs the cluster page.
export interface HostInfo {
  instanceId: string;
  name: string | null;
  environment: string | null;
  lastSeen: string;
  totalExecutions: number;
  currentlyRunning: number;
}

// Per-train run roll-up (train detail summary cards).
export interface TrainExecutionStats {
  trainName: string;
  total: number;
  completed: number;
  failed: number;
  inProgress: number;
  pending: number;
  cancelled: number;
  lastRun: string | null;
  lastSuccessfulRun: string | null;
  averageMilliseconds: number | null;
}

// Per-manifest run roll-up (manifest detail summary cards).
export interface ManifestExecutionStats {
  manifestId: number;
  total: number;
  completed: number;
  failed: number;
  inProgress: number;
  pending: number;
  cancelled: number;
  lastRun: string | null;
  lastSuccessfulRun: string | null;
}

// Per-group roll-up, fetched in a batch for the visible page of the groups list.
export interface ManifestGroupStats {
  groupId: number;
  manifestCount: number;
  totalExecutions: number;
  completed: number;
  failed: number;
  inProgress: number;
  lastRun: string | null;
}

export type ExclusionType =
  | "DAYS_OF_WEEK"
  | "DATES"
  | "DATE_RANGE"
  | "TIME_WINDOW";

export type DayOfWeek =
  | "SUNDAY"
  | "MONDAY"
  | "TUESDAY"
  | "WEDNESDAY"
  | "THURSDAY"
  | "FRIDAY"
  | "SATURDAY";

// A manifest schedule exclusion window. Flat discriminated shape: `type` selects which fields
// apply. `dates`/`startDate`/`endDate` are ISO date strings; `startTime`/`endTime` are LocalTime
// ("HH:mm:ss").
export interface ManifestExclusion {
  type: ExclusionType;
  daysOfWeek: DayOfWeek[] | null;
  dates: string[] | null;
  startDate: string | null;
  endDate: string | null;
  startTime: string | null;
  endTime: string | null;
}

// A registered effect and its runtime state in the API process.
export interface EffectInfo {
  name: string;
  fullName: string;
  enabled: boolean;
  toggleable: boolean;
  isConfigurable?: boolean;
  configurationTypeName?: string | null;
  // The effect's configuration as JSON, sensitive values already redacted by the API.
  configuration?: string | null;
  // One per public read-write setting, editable ones first; empty when not configurable.
  // Optional because older fixtures predate it.
  fields?: EffectSettingInfo[];
}

// How a setting is edited: a switch, one of enumValues, text read as the setting's type, or not
// at all (a delegate, collection or object set in code).
export type EffectFieldKind = "BOOLEAN" | "ENUM" | "TEXT" | "SET_IN_CODE";

// One setting of a configurable effect, as operations.effects describes it for an editor.
export interface EffectSettingInfo {
  name: string;
  typeName: string;
  kind: EffectFieldKind;
  nullable: boolean;
  enumValues: string[] | null;
  // [TraxSensitive]: value is always null, hasValue says whether one is set.
  sensitive: boolean;
  hasValue: boolean;
  // The current value as text, in the form configureEffect reads back. Null when there is none,
  // when sensitive, and for SET_IN_CODE.
  value: string | null;
  // What to type, such as "Enter a whole number".
  hint: string;
}

export interface EffectSettingError {
  field: string;
  message: string;
}

// configureEffect: all or nothing. errors names each refused setting.
export interface ConfigureEffectResponse {
  success: boolean;
  count: number;
  message: string;
  errors: EffectSettingError[];
}

export interface LogLevelSetting {
  category: string;
  // The level its loggers filter at now, by LogLevel name ("Trace" ... "None").
  level: string;
  // The value configured under Logging:LogLevel; null when the category is not configured there.
  configuredLevel?: string | null;
  // Whether a level was set at runtime, so level is not the configured one.
  overridden?: boolean;
}

// setLogLevels: notApplied lists categories set whose loggers still filter at another level.
export interface SetLogLevelsResponse {
  success: boolean;
  count: number;
  notApplied: string[];
  message: string;
}

export interface SchedulerConfigSnapshot {
  manifestManagerEnabled: boolean;
  jobDispatcherEnabled: boolean;
  manifestManagerPollingInterval: string;
  jobDispatcherPollingInterval: string;
  maxActiveJobs: number | null;
  defaultMaxRetries: number;
  defaultRetryDelay: string;
  retryBackoffMultiplier: number;
  maxRetryDelay: string;
  defaultJobTimeout: string;
  stalePendingTimeout: string;
  recoverStuckJobsOnStartup: boolean;
  deadLetterRetentionPeriod: string;
  autoPurgeDeadLetters: boolean;
  localWorkerCount: number | null;
  metadataCleanupInterval: string | null;
  metadataCleanupRetention: string | null;
  // Optional because older fixtures predate it.
  failureCountWindow?: string;
}

export interface OperationResponse {
  success: boolean;
  count: number | null;
  message: string | null;
  // The created row: a work queue entry for queueTrain, a run for runTrain / requeueExecution.
  id?: number | null;
}

// One id a batch trigger did not trigger as asked (not found, or too late to ask afresh).
export interface BatchTriggerNote {
  id: number;
  message: string;
}

// triggerManifests / triggerGroups. success is false only when the batch was refused as given.
export interface BatchTriggerResponse {
  success: boolean;
  matched: number;
  queued: number;
  alreadyQueued: number;
  tooLateToAskAfresh: number;
  skipped: number;
  message: string;
  notes: BatchTriggerNote[];
}

export interface DeadLetterOperationResult {
  success: boolean;
  workQueueId: number | null;
  message: string | null;
}

// Payload published by the onTrain* lifecycle subscriptions.
export interface TrainLifecycleEvent {
  metadataId: number;
  externalId: string;
  trainName: string;
  trainState: TrainState;
  timestamp: string;
  failureJunction: string | null;
  failureReason: string | null;
}

// ── Junction timeline ──────────────────────────────────────────────────────
export type JunctionRunKind = "JUNCTION" | "CHOICE" | "SCORE" | "YES_NO" | "ROUTE";
export type JunctionRunState = "IN_PROGRESS" | "COMPLETED" | "FAILED" | "CANCELLED";
export type JunctionEventType =
  | "JUNCTION_STARTED"
  | "JUNCTION_COMPLETED"
  | "JUNCTION_FAILED"
  | "JUNCTION_CANCELLED"
  | "DECIDED"
  | "DECISION_REFUSED"
  | "ROUTED";

// One step of a run, as operations.junctionRuns and onJunctionEvent carry it. A withheld answer
// is already absent; a withheld name is shown as "withheld".
export interface JunctionStep {
  position: number;
  kind: JunctionRunKind;
  name: string;
  state: JunctionRunState;
  startedAt: string;
  endedAt: string | null;
  durationMs: number | null;
  failureClass: FailureClass | null;
  failureException: string | null;
  questionKey: string | null;
  answer: string | null;
  confidence: number | null;
  replayed: boolean;
  decider: string | null;
  answerWithheld: boolean;
  attempt: number | null;
  nameWithheld: boolean;
  trackPosition: number | null;
}

export interface JunctionEvent {
  metadataId: number;
  externalId: string;
  trainName: string;
  eventType: JunctionEventType;
  timestamp: string;
  junction: JunctionStep;
  // 1 for the first event on this subscription; a jump of more than one means events were lost.
  sequence: number;
}

// ── Recorded decisions ─────────────────────────────────────────────────────
// One decision a run recorded (operations.decisions). A withheld answer (a [TraxSensitive]
// question) arrives with answer, refused, replayRefused, shadows and routes null; on a withheld
// track only id, metadataId, occurrence, replayed, isRefused and decidedAt are kept.
export interface DecisionRecord {
  id: number;
  metadataId: number;
  questionKey: string | null;
  occurrence: number;
  kind: string | null;
  question: string | null;
  answer: string | null;
  refused: string | null;
  isRefused: boolean;
  fingerprint: string | null;
  model: string | null;
  decider: string | null;
  replayed: boolean;
  shadows: string | null;
  routes: string | null;
  stateHash: string | null;
  decidedAt: string;
  answerWithheld: boolean;
  trackWithheld: boolean;
  replayRefused: string | null;
}

export interface DecisionPage {
  items: DecisionRecord[];
  take: number;
  // The last decision's id: pass it as afterId for the next page. Null on an empty page.
  nextCursor: number | null;
}

// ── Persisted operations ───────────────────────────────────────────────────
export interface PersistedOperation {
  id: string;
  tenantKey: string | null;
  operationName: string;
  version: number;
  document: string;
  shapeFingerprint: string;
  isActive: boolean;
  deprecationReason: string | null;
  description: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface PersistedOperationHistoryEntry {
  historyId: number;
  id: string;
  tenantKey: string | null;
  document: string;
  shapeFingerprint: string;
  changeType: string;
  changedAt: string;
  changedReason: string | null;
}

export interface PersistedOperationError {
  code: string;
  message: string;
  locations: { line: number; column: number }[] | null;
  path: string[] | null;
  oldFingerprint: string | null;
  newFingerprint: string | null;
}

export interface PersistedOperationPayload {
  success: boolean;
  operation: PersistedOperation | null;
  errors: PersistedOperationError[];
}

// ── Run graph ──────────────────────────────────────────────────────────────
export type ChainStepKind =
  | "CHAIN"
  | "CHECKPOINT"
  | "DECIDE"
  | "EXTRACT"
  | "GATE"
  | "I_CHAIN"
  | "PARALLEL"
  | "RESOLVE"
  | "SCALE"
  | "SEED"
  | "SHORT_CIRCUIT"
  | "SWITCH";

export type RunNodeState =
  | "NOT_REACHED"
  | "IN_PROGRESS"
  | "COMPLETED"
  | "FAILED"
  | "CANCELLED"
  | "SKIPPED"
  | "NOT_RECORDED"
  | "WITHHELD"
  | "RESTORED";

// The steps a node recorded, as far as the run graph draws them.
export type RunGraphStep = Pick<JunctionStep, "state" | "failureClass" | "failureException">;

// One declared step of a run's train, with where the run left it (operations.runGraph). A node
// says whether a checkpoint is stored at it and whether resumeExecution can resume there; never
// what a checkpoint holds.
export interface RunGraphNode {
  id: string;
  kind: ChainStepKind;
  opaque: boolean;
  replayed: boolean;
  checkpointed: boolean;
  canResume: boolean;
  state: RunNodeState;
  steps: RunGraphStep[];
  // Absent below the depth RUN_GRAPH reads.
  tracks?: RunGraphTrack[];
}

export interface RunGraphTrack {
  name: string;
  description: string | null;
  isFallback: boolean;
  taken: boolean;
  nodes: RunGraphNode[];
}

export interface RunGraph {
  metadataId: number;
  hasGraph: boolean;
  moreSteps: boolean;
  // True when resumeExecution can resume the run after its latest checkpoint.
  canResume: boolean;
  nodes: RunGraphNode[];
  unmatchedSteps: Pick<JunctionStep, "position" | "name" | "nameWithheld" | "state">[];
}

// ── State machines ─────────────────────────────────────────────────────────
// An instance as operators see it: never its context, never whose a user's draft is.
export type SnapshotOwnerKind = "SYSTEM" | "USER";

export interface MachineInstance {
  machine: string;
  ownerKind: SnapshotOwnerKind;
  id: string;
  rowId: number;
  state: string;
  version: number;
  createdAt: string | null;
  updatedAt: string;
  // True while the instance's state waits on a train run it invoked.
  hasLiveInvokedRun: boolean;
}

export interface MachineInstanceCount {
  machine: string;
  state: string;
  ownerKind: SnapshotOwnerKind;
  count: number;
}

export interface MachineInstanceInvokedRun {
  id: number;
  externalId: string;
  name: string;
  trainState: TrainState;
  startTime: string;
  endTime: string | null;
  failureClass: FailureClass;
  cancellationRequested: boolean;
  // The run the instance's state waits on now.
  isLive: boolean;
}

export interface MachineInstanceDetail extends MachineInstance {
  invokedRuns: MachineInstanceInvokedRun[];
  // The instance invoked more runs than the newest 50 listed.
  isInvokedRunsCapped: boolean;
  // The work queue entry of the run its state waits on, while that run is still queued.
  queuedInvokedRunEntryId: number | null;
}

export type MachineInstanceCancelOutcome =
  | "CANCEL_REQUESTED"
  | "MOVED"
  | "NOT_FOUND"
  | "NO_LIVE_RUN"
  | "RUN_CANCELLED"
  | "RUN_ENDED"
  | "USER_OWNED";

export interface MachineInstanceCancelResponse {
  success: boolean;
  outcome: MachineInstanceCancelOutcome;
  message: string;
  // The state the instance moved into, for MOVED.
  state: string | null;
}
