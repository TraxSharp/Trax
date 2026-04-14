import { gql } from "@urql/core";

export const LIST_JOBS = gql`
  query ListJobs($input: ListJobsInput!) {
    discover {
      listJobs(input: $input) {
        jobs {
          id
          title
          company
          url
          status
          createdAt
        }
      }
    }
  }
`;

export const ADD_JOB = gql`
  mutation AddJob($input: AddJobInput!) {
    dispatch {
      addJob(input: $input) {
        externalId
        output {
          jobId
          userId
          title
          company
        }
      }
    }
  }
`;

export const GET_PROFILE = gql`
  query GetProfile($input: GetProfileInput!) {
    discover {
      getProfile(input: $input) {
        userId
        skillsJson
        educationJson
        workHistoryJson
      }
    }
  }
`;

export const UPDATE_PROFILE = gql`
  mutation UpdateProfile($input: UpdateProfileInput!) {
    dispatch {
      updateProfile(input: $input) {
        output {
          userId
          facet
          updatedAt
        }
      }
    }
  }
`;

export const GENERATE_MATERIALS = gql`
  mutation GenerateMaterials($input: GenerateApplicationMaterialsInput!) {
    dispatch {
      generateApplicationMaterials(input: $input) {
        externalId
        output {
          resumeArtifactId
          coverLetterArtifactId
          resumeMarkdown
          coverLetterMarkdown
        }
      }
    }
  }
`;

export const GET_ARTIFACTS = gql`
  query GetArtifacts($input: GetArtifactsInput!) {
    discover {
      getArtifacts(input: $input) {
        artifacts {
          id
          type
          content
          modelUsed
          generatedAt
        }
      }
    }
  }
`;

export const LIST_APPLICATIONS = gql`
  query ListApplications($input: ListApplicationsInput!) {
    discover {
      listApplications(input: $input) {
        applications {
          id
          jobId
          status
          createdAt
        }
      }
    }
  }
`;

export const CREATE_APPLICATION = gql`
  mutation CreateApplication($input: CreateApplicationInput!) {
    dispatch {
      createApplication(input: $input) {
        output {
          applicationId
          status
        }
      }
    }
  }
`;

export const FIND_CONTACT = gql`
  mutation FindContact($input: FindContactInput!) {
    dispatch {
      findContact(input: $input) {
        output {
          contactId
          name
          email
          source
        }
      }
    }
  }
`;

export const WATCH_COMPANY = gql`
  mutation WatchCompany($input: WatchCompanyInput!) {
    dispatch {
      watchCompany(input: $input) {
        output {
          watchedCompanyId
          companyName
        }
      }
    }
  }
`;

export const LIST_WATCHED_COMPANIES = gql`
  query ListWatchedCompanies($input: ListWatchedCompaniesInput!) {
    discover {
      listWatchedCompanies(input: $input) {
        companies {
          id
          companyName
          careersUrl
          lastCheckedAt
        }
      }
    }
  }
`;

export const ON_USER_JOBS = gql`
  subscription OnUserJobs($userId: String!) {
    onUserJobs(userId: $userId) {
      eventType
      payload
      timestamp
    }
  }
`;

export const ON_JOB_MATERIALS = gql`
  subscription OnJobMaterials($jobId: UUID!) {
    onJobMaterials(jobId: $jobId) {
      eventType
      payload
      timestamp
    }
  }
`;
