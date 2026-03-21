import { gql } from "@apollo/client";

export const ON_TRAIN_COMPLETED = gql`
  subscription OnTrainCompleted {
    onTrainCompleted {
      externalId
      trainName
      output
    }
  }
`;

export const ON_TRAIN_FAILED = gql`
  subscription OnTrainFailed {
    onTrainFailed {
      externalId
      trainName
      failureReason
    }
  }
`;
