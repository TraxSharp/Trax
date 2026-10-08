import { gql } from "@urql/core";

// Dead letter resolution. requeue creates a fresh work queue entry and marks the dead
// letter Retried; acknowledge closes it with an operator note without retrying. askAfresh makes
// the new run ask the model afresh instead of replaying the failed run's decisions.
export const REQUEUE_DEAD_LETTER = gql`
  mutation RequeueDeadLetter($id: Long!, $askAfresh: Boolean! = false) {
    operations {
      deadLetters {
        requeueDeadLetter(id: $id, askAfresh: $askAfresh) {
          success
          workQueueId
          message
        }
      }
    }
  }
`;

export const ACKNOWLEDGE_DEAD_LETTER = gql`
  mutation AcknowledgeDeadLetter($id: Long!, $note: String!) {
    operations {
      deadLetters {
        acknowledgeDeadLetter(id: $id, note: $note) {
          success
          message
        }
      }
    }
  }
`;

// Queue a train for background execution. id is the new work queue entry.
export const QUEUE_TRAIN = gql`
  mutation QueueTrain($input: QueueTrainInput!) {
    operations {
      workQueue {
        queueTrain(input: $input) {
          success
          message
          id
        }
      }
    }
  }
`;

// Batch requeue/acknowledge dead letters (bulk selection).
export const REQUEUE_DEAD_LETTERS = gql`
  mutation RequeueDeadLetters($ids: [Long!]!, $askAfresh: Boolean! = false) {
    operations {
      deadLetters {
        requeueDeadLetters(ids: $ids, askAfresh: $askAfresh) {
          count
          message
        }
      }
    }
  }
`;

export const ACKNOWLEDGE_DEAD_LETTERS = gql`
  mutation AcknowledgeDeadLetters($ids: [Long!]!, $note: String!) {
    operations {
      deadLetters {
        acknowledgeDeadLetters(ids: $ids, note: $note) {
          count
          message
        }
      }
    }
  }
`;

// Requeue / acknowledge EVERY unresolved dead letter in one call (not just a selection).
// Requeue-all starts a background job on the node that received it and returns the job at once;
// REQUEUE_ALL_JOB reads it back until its status is no longer RUNNING.
export const REQUEUE_ALL_DEAD_LETTERS = gql`
  mutation RequeueAllDeadLetters($askAfresh: Boolean! = false) {
    operations {
      deadLetters {
        requeueAllDeadLetters(askAfresh: $askAfresh) {
          id
          status
          started
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

export const ACKNOWLEDGE_ALL_DEAD_LETTERS = gql`
  mutation AcknowledgeAllDeadLetters($note: String!) {
    operations {
      deadLetters {
        acknowledgeAllDeadLetters(note: $note) {
          count
          message
        }
      }
    }
  }
`;

// Batch cancel queued work queue entries (bulk selection).
export const CANCEL_WORK_QUEUE_ENTRIES = gql`
  mutation CancelWorkQueueEntries($ids: [Long!]!) {
    operations {
      workQueue {
        cancelWorkQueueEntries(ids: $ids) {
          success
          count
          message
        }
      }
    }
  }
`;

// Cancel / re-queue a single execution.
export const CANCEL_EXECUTION = gql`
  mutation CancelExecution($id: Long!) {
    operations {
      cancelExecution(id: $id) {
        success
        count
        message
      }
    }
  }
`;

// Cancel several executions in one call (bulk selection). count is how many were flagged.
export const CANCEL_EXECUTIONS = gql`
  mutation CancelExecutions($ids: [Long!]!) {
    operations {
      cancelExecutions(ids: $ids) {
        success
        count
        message
      }
    }
  }
`;

// Re-queue a run with its saved input. askAfresh: the new run asks the model afresh instead of
// replaying this run's decisions. id is the new work queue entry.
export const REQUEUE_EXECUTION = gql`
  mutation RequeueExecution($id: Long!, $askAfresh: Boolean! = false) {
    operations {
      requeueExecution(id: $id, askAfresh: $askAfresh) {
        success
        message
        id
      }
    }
  }
`;

// Resume a failed or cancelled run from a checkpoint instead of running every step again: at the
// node `from` names (a run graph node id), or after its latest checkpoint when from is null. id is
// the new work queue entry; a refusal is success: false with the reason.
export const RESUME_EXECUTION = gql`
  mutation ResumeExecution($id: Long!, $from: String) {
    operations {
      resumeExecution(id: $id, from: $from) {
        success
        message
        id
      }
    }
  }
`;

// Patch a manifest's mutable settings.
export const UPDATE_MANIFEST = gql`
  mutation UpdateManifest($id: Long!, $input: UpdateManifestInput!) {
    operations {
      updateManifest(id: $id, input: $input) {
        success
        message
      }
    }
  }
`;

// Cancel a queued work queue entry (only entries in QUEUED can be cancelled).
export const CANCEL_WORK_QUEUE_ENTRY = gql`
  mutation CancelWorkQueueEntry($id: Long!) {
    operations {
      workQueue {
        cancelWorkQueueEntry(id: $id) {
          success
          message
        }
      }
    }
  }
`;

// Manifest controls. trigger enqueues an immediate run; enable/disable toggle scheduling.
// askAfresh: a queued retry the trigger releases asks the model afresh instead of replaying.
export const TRIGGER_MANIFEST = gql`
  mutation TriggerManifest($externalId: String!, $askAfresh: Boolean! = false) {
    operations {
      triggerManifest(externalId: $externalId, askAfresh: $askAfresh) {
        success
        message
      }
    }
  }
`;

export const ENABLE_MANIFEST = gql`
  mutation EnableManifest($externalId: String!) {
    operations {
      enableManifest(externalId: $externalId) {
        success
        message
      }
    }
  }
`;

export const DISABLE_MANIFEST = gql`
  mutation DisableManifest($externalId: String!) {
    operations {
      disableManifest(externalId: $externalId) {
        success
        message
      }
    }
  }
`;

// Trigger a manifest after a delay, and cancel every running execution of a manifest.
export const TRIGGER_MANIFEST_DELAYED = gql`
  mutation TriggerManifestDelayed($externalId: String!, $delay: Duration!) {
    operations {
      triggerManifestDelayed(externalId: $externalId, delay: $delay) {
        success
        message
      }
    }
  }
`;

export const CANCEL_MANIFEST = gql`
  mutation CancelManifest($externalId: String!) {
    operations {
      cancelManifest(externalId: $externalId) {
        success
        count
        message
      }
    }
  }
`;

// Trigger many manifests by database id in one call, as the Blazor Trigger Selected does. Each
// manifest gets an immediate run, or its queued entry is brought forward. notes names each id
// that was not triggered as asked.
const BATCH_TRIGGER_PAYLOAD = `
  success
  matched
  queued
  alreadyQueued
  tooLateToAskAfresh
  skipped
  message
  notes {
    id
    message
  }
`;

export const TRIGGER_MANIFESTS = gql`
  mutation TriggerManifests($ids: [Long!]!, $askAfresh: Boolean! = false) {
    operations {
      triggerManifests(ids: $ids, askAfresh: $askAfresh) {
        ${BATCH_TRIGGER_PAYLOAD}
      }
    }
  }
`;

// Trigger / cancel the running executions of many manifest groups in one call.
export const TRIGGER_GROUPS = gql`
  mutation TriggerGroups($ids: [Long!]!) {
    operations {
      triggerGroups(ids: $ids) {
        ${BATCH_TRIGGER_PAYLOAD}
      }
    }
  }
`;

export const CANCEL_GROUPS = gql`
  mutation CancelGroups($ids: [Long!]!) {
    operations {
      cancelGroups(ids: $ids) {
        success
        count
        message
      }
    }
  }
`;

// Manifest group controls: trigger/cancel every manifest in the group, and patch its settings.
export const TRIGGER_GROUP = gql`
  mutation TriggerGroup($groupId: Long!) {
    operations {
      triggerGroup(groupId: $groupId) {
        success
        count
        message
      }
    }
  }
`;

export const CANCEL_GROUP = gql`
  mutation CancelGroup($groupId: Long!) {
    operations {
      cancelGroup(groupId: $groupId) {
        success
        count
        message
      }
    }
  }
`;

export const UPDATE_MANIFEST_GROUP = gql`
  mutation UpdateManifestGroup($id: Long!, $input: UpdateManifestGroupInput!) {
    operations {
      manifestGroups {
        updateManifestGroup(id: $id, input: $input) {
          success
          message
        }
      }
    }
  }
`;

// Patch mutable scheduler runtime settings.
export const UPDATE_SCHEDULER = gql`
  mutation UpdateScheduler($input: UpdateSchedulerConfigInput!) {
    operations {
      config {
        updateScheduler(input: $input) {
          success
          message
        }
      }
    }
  }
`;

// Run a train now, in the API process, through the host's job submitter (no work queue entry).
// id is the new run (metadata row).
export const RUN_TRAIN = gql`
  mutation RunTrain($input: RunTrainInput!) {
    operations {
      workQueue {
        runTrain(input: $input) {
          success
          message
          id
        }
      }
    }
  }
`;

// Enable / disable several manifests in one call. Only manifests whose flag differs are written,
// and count says how many that was.
export const SET_MANIFESTS_ENABLED = gql`
  mutation SetManifestsEnabled($ids: [Long!]!, $enabled: Boolean!) {
    operations {
      setManifestsEnabled(ids: $ids, enabled: $enabled) {
        success
        count
        message
      }
    }
  }
`;

// Whether a retry of a failed run replays the decisions that run recorded (true) or asks the
// model afresh (false), for several manifests in one call.
export const SET_MANIFESTS_REPLAY_DECISIONS_ON_RETRY = gql`
  mutation SetManifestsReplayDecisionsOnRetry($ids: [Long!]!, $replay: Boolean!) {
    operations {
      setManifestsReplayDecisionsOnRetry(ids: $ids, replay: $replay) {
        success
        count
        message
      }
    }
  }
`;

// Enable / disable the selected manifest groups, or every group.
export const SET_MANIFEST_GROUPS_ENABLED = gql`
  mutation SetManifestGroupsEnabled($ids: [Long!]!, $enabled: Boolean!) {
    operations {
      manifestGroups {
        setManifestGroupsEnabled(ids: $ids, enabled: $enabled) {
          success
          count
          message
        }
      }
    }
  }
`;

export const SET_ALL_MANIFEST_GROUPS_ENABLED = gql`
  mutation SetAllManifestGroupsEnabled($enabled: Boolean!) {
    operations {
      manifestGroups {
        setAllManifestGroupsEnabled(enabled: $enabled) {
          success
          count
          message
        }
      }
    }
  }
`;

// Turn a toggleable effect on or off in the API process (applies to the next train scope).
export const SET_EFFECT_ENABLED = gql`
  mutation SetEffectEnabled($fullName: String!, $enabled: Boolean!) {
    operations {
      setEffectEnabled(fullName: $fullName, enabled: $enabled) {
        success
        message
      }
    }
  }
`;

// Write the changed settings of a configurable effect in the API process, all or none. A value
// is text read as the setting's type; null clears a nullable setting.
export const CONFIGURE_EFFECT = gql`
  mutation ConfigureEffect($fullName: String!, $values: [EffectSettingValueInput!]!) {
    operations {
      configureEffect(fullName: $fullName, values: $values) {
        success
        count
        message
        errors {
          field
          message
        }
      }
    }
  }
`;

// Set the level configured log categories filter at in the API process, until it restarts.
export const SET_LOG_LEVELS = gql`
  mutation SetLogLevels($levels: [LogLevelSettingInput!]!) {
    operations {
      config {
        setLogLevels(levels: $levels) {
          success
          count
          notApplied
          message
        }
      }
    }
  }
`;

// ── Persisted operations ───────────────────────────────────────────────────
// Each returns success plus structured errors with a stable code (PARSE_FAILED,
// SCHEMA_VALIDATION_FAILED, SHAPE_DIFF_VIOLATION, INVALID_INPUT, NOT_FOUND, ...); none throws.
const PERSISTED_OPERATION_PAYLOAD = `
  success
  errors {
    code
    message
    locations {
      line
      column
    }
    path
    oldFingerprint
    newFingerprint
  }
  operation {
    id
    tenantKey
    version
    isActive
    shapeFingerprint
  }
`;

export const UPLOAD_PERSISTED_OPERATION = gql`
  mutation UploadPersistedOperation($input: UploadPersistedOperationInput!) {
    operations {
      persistedOperations {
        uploadPersistedOperation(input: $input) {
          ${PERSISTED_OPERATION_PAYLOAD}
        }
      }
    }
  }
`;

export const DEACTIVATE_PERSISTED_OPERATION = gql`
  mutation DeactivatePersistedOperation($input: DeactivatePersistedOperationInput!) {
    operations {
      persistedOperations {
        deactivatePersistedOperation(input: $input) {
          ${PERSISTED_OPERATION_PAYLOAD}
        }
      }
    }
  }
`;

export const RESTORE_PERSISTED_OPERATION = gql`
  mutation RestorePersistedOperation($input: RestorePersistedOperationInput!) {
    operations {
      persistedOperations {
        restorePersistedOperation(input: $input) {
          ${PERSISTED_OPERATION_PAYLOAD}
        }
      }
    }
  }
`;
