import { gql } from "@apollo/client";

const STEP = `
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
  answerWithheld
  nameWithheld
  trackPosition
  attempt
  nodeId
`;

// The sample's own mutation: schedules a one-off manifest (Trax.Api has no ScheduleOnce operation).
export const START_RUN = gql`
  mutation StartRun($input: StartRunInput!) {
    dispatch {
      startRun(input: $input) {
        output {
          runId
          manifestId
          manifestExternalId
          trainName
          armedCrash
          maxRetries
        }
      }
    }
  }
`;

export const CHANGE_CASE_DATA = gql`
  mutation ChangeCaseData($runId: String!) {
    dispatch {
      changeCaseData(input: { runId: $runId }) {
        output {
          change
        }
      }
    }
  }
`;

// Every attempt of the manifest: the first run, then each retry.
export const EXECUTIONS = gql`
  query Executions($manifestId: Long!) {
    operations {
      executions(manifestId: $manifestId, order: OLDEST, take: 20) {
        items {
          id
          trainState
          startTime
          endTime
          failureJunction
          failureReason
        }
      }
    }
  }
`;

export const EXECUTION = gql`
  query Execution($id: Long!) {
    operations {
      execution(id: $id) {
        id
        trainState
        startTime
        endTime
        failureJunction
        failureReason
      }
    }
  }
`;

// Subscribe first, then read the stored steps and merge by position: rows trail the stream.
export const ON_JUNCTION_EVENT = gql`
  subscription OnJunctionEvent($metadataId: Long!) {
    onJunctionEvent(metadataId: $metadataId) {
      eventType
      sequence
      junction { ${STEP} }
    }
  }
`;

export const JUNCTION_RUNS = gql`
  query JunctionRuns($metadataId: Long!) {
    operations {
      junctionRuns(metadataId: $metadataId) { ${STEP} }
    }
  }
`;

// The sample's query over trax.decision: why an answer was asked afresh.
export const DECISION_JOURNAL = gql`
  query DecisionJournal($metadataId: Long!) {
    discover {
      decisionJournal(input: { metadataId: $metadataId }) {
        replayDecisionsOf
        replayAbandoned
        decisions {
          questionKey
          occurrence
          replayed
          replayRefused
          model
          stateHash
        }
      }
    }
  }
`;

export const TRIGGER_ASK_AFRESH = gql`
  mutation TriggerAskAfresh($externalId: String!) {
    operations {
      triggerManifest(externalId: $externalId, askAfresh: true) {
        success
        message
      }
    }
  }
`;

export const REQUEUE = gql`
  mutation Requeue($id: Long!, $askAfresh: Boolean!) {
    operations {
      requeueExecution(id: $id, askAfresh: $askAfresh) {
        success
        message
        id
      }
    }
  }
`;

// Resumes a failed or cancelled run at a step (a node id from the run graph), restoring the checkpoint
// before it: the steps before the point are skipped, not run. Answers with the work queue entry's id.
export const RESUME = gql`
  mutation Resume($id: Long!, $from: String) {
    operations {
      resumeExecution(id: $id, from: $from) {
        success
        message
        id
      }
    }
  }
`;

// requeueExecution and resumeExecution answer with the work queue entry's id; its run appears once dispatched.
export const WORK_QUEUE_ENTRY = gql`
  query WorkQueueEntry($id: Long!) {
    operations {
      workQueue {
        workQueue(id: $id) {
          id
          status
          metadataId
        }
      }
    }
  }
`;

// The run drawn on its train's declared chain: every node, its state, each routing step's tracks
// and each Parallel step's branches. Read as allNodes, one flat list at any depth, which treeOf
// rebuilds from each node's parentId and track: a query that followed nested tracks could go only
// as deep as the server's field-cycle limit allows.
export const RUN_GRAPH = gql`
  query RunGraph($metadataId: Long!) {
    operations {
      runGraph(metadataId: $metadataId) {
        hasGraph
        allNodes {
          id
          kind
          junction
          state
          replayed
          trackTaken
          canResume
          checkpointed
          parentId
          track
          tracks {
            name
            taken
          }
        }
      }
    }
  }
`;
