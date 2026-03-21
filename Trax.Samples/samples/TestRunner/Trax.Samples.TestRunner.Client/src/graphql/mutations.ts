import { gql } from "@apollo/client";

export const RUN_TESTS = gql`
  mutation RunTests($input: RunTestsInput!) {
    dispatch {
      runTests(input: $input) {
        externalId
      }
    }
  }
`;
