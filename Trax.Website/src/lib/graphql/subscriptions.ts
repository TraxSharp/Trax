import { gql } from "@apollo/client/core";

export const ON_TRAIN_STARTED = gql`
  subscription OnTrainStarted {
    onTrainStarted {
      metadataId
      externalId
      trainName
      trainState
      timestamp
    }
  }
`;

export const ON_TRAIN_COMPLETED = gql`
  subscription OnTrainCompleted {
    onTrainCompleted {
      metadataId
      externalId
      trainName
      trainState
      timestamp
    }
  }
`;

export const ON_TRAIN_FAILED = gql`
  subscription OnTrainFailed {
    onTrainFailed {
      metadataId
      externalId
      trainName
      trainState
      timestamp
      failureStep
      failureReason
    }
  }
`;

export const ON_TRAIN_STATE_CHANGED = gql`
  subscription OnTrainStateChanged {
    onTrainStateChanged {
      metadataId
      externalId
      trainName
      trainState
      timestamp
    }
  }
`;
