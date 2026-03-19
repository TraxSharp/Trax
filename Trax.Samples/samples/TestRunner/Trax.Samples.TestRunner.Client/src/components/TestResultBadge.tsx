import { TestResult } from "../types";

interface TestResultBadgeProps {
  result: TestResult;
}

export function TestResultBadge({ result }: TestResultBadgeProps) {
  const allPassed = result.failed === 0;

  return (
    <span className={`result-badge ${allPassed ? "badge-pass" : "badge-fail"}`}>
      {allPassed
        ? `${result.passed}/${result.total} passed`
        : `${result.failed} failed`}
    </span>
  );
}
