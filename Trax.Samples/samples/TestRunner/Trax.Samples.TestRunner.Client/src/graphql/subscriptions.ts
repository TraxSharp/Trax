import { gql } from "@apollo/client";

export const ON_TRAIN_COMPLETED = gql`
  subscription OnTrainCompleted {
    onTrainCompleted {
      metadataId
      externalId
      trainName
      trainState
      timestamp
      output
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
      failureReason
    }
  }
`;
