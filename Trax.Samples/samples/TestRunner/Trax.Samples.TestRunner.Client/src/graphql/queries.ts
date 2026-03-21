import { gql } from "@apollo/client";

export const DISCOVER_TEST_PROJECTS = gql`
  query DiscoverTestProjects {
    discover {
      discoverTestProjects {
        projects {
          name
          projectPath
        }
      }
    }
  }
`;
