import { gql } from "@apollo/client/core";

export const HEALTH_QUERY = gql`
  query Health {
    operations {
      health {
        status
        description
        queueDepth
        inProgress
        failedLastHour
        deadLetters
      }
    }
  }
`;

export const TRAINS_QUERY = gql`
  query Trains {
    operations {
      trains {
        serviceTypeName
        implementationTypeName
        inputTypeName
        outputTypeName
        lifetime
        isQuery
        isMutation
        graphQLName
      }
    }
  }
`;

export const MANIFESTS_QUERY = gql`
  query Manifests($skip: Int, $take: Int) {
    operations {
      manifests(skip: $skip, take: $take) {
        items {
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
          priority
        }
        totalCount
      }
    }
  }
`;

export const EXECUTIONS_QUERY = gql`
  query Executions($skip: Int, $take: Int) {
    operations {
      executions(skip: $skip, take: $take) {
        items {
          id
          externalId
          name
          trainState
          startTime
          endTime
          failureJunction
          failureReason
          cancellationRequested
        }
        totalCount
      }
    }
  }
`;
