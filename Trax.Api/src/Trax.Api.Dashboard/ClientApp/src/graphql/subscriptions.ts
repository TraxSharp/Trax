import { gql } from "@urql/core";

// Real-time execution feed. onTrainStateChanged fires on every lifecycle transition
// (started, completed, failed, cancelled), so one subscription drives the whole feed.
// Delivered over the graphql-ws WebSocket; the API key rides in the connection_init
// payload (see lib/graphql.ts).
export const ON_TRAIN_STATE_CHANGED = gql`
  subscription OnTrainStateChanged {
    onTrainStateChanged {
      metadataId
      externalId
      trainName
      trainState
      timestamp
      failureJunction
      failureReason
    }
  }
`;

// Coalesced data-change signal. Fires once per burst of writes to a domain (work queue, dead
// letters, manifests, manifest groups, scheduler config) and carries only which domain changed,
// so a list page can refetch its bounded view instead of polling. See lib/useRefetchOnChange.
export const ON_DATA_CHANGED = gql`
  subscription OnDataChanged {
    onDataChanged {
      domain
      timestamp
    }
  }
`;

// One run's steps as they start and end (junction, decision, route). Each event carries the whole
// step, so the timeline upserts it by position. Lossy under load: a sequence jump means events
// were missed, and the timeline reads operations.junctionRuns again.
export const ON_JUNCTION_EVENT = gql`
  subscription OnJunctionEvent($metadataId: Long!) {
    onJunctionEvent(metadataId: $metadataId) {
      metadataId
      eventType
      timestamp
      sequence
      junction {
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
