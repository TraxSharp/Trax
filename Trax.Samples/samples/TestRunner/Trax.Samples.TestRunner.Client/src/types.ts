export interface TestProject {
  name: string;
  projectPath: string;
}

export interface TestCaseResult {
  fullName: string;
  outcome: string;
  durationSeconds: number;
  errorMessage: string | null;
  stackTrace: string | null;
}

export interface TestResult {
  projectName: string;
  total: number;
  passed: number;
  failed: number;
  skipped: number;
  durationSeconds: number;
  failedTests: TestCaseResult[];
}

export interface TestRunState {
  projectName: string;
  externalId: string;
  status: "queued" | "completed" | "failed";
  result?: TestResult;
  failureReason?: string;
}
