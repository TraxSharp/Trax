import { useState } from "react";
import { TestRunState } from "../types";
import { TestResultBadge } from "./TestResultBadge";

interface TestRunCardProps {
  run: TestRunState;
}

export function TestRunCard({ run }: TestRunCardProps) {
  const [expanded, setExpanded] = useState(false);

  if (run.status === "queued") {
    return (
      <div className="run-card run-card-pending">
        <div className="run-card-header">
          <span className="run-card-name">{run.projectName}</span>
          <span className="run-card-status">Running...</span>
        </div>
        <div className="spinner" />
      </div>
    );
  }

  if (run.status === "failed") {
    return (
      <div className="run-card run-card-error">
        <div className="run-card-header">
          <span className="run-card-name">{run.projectName}</span>
          <span className="result-badge badge-fail">Error</span>
        </div>
        {run.failureReason && (
          <pre className="error-message">{run.failureReason}</pre>
        )}
      </div>
    );
  }

  const result = run.result!;

  return (
    <div
      className={`run-card ${result.failed > 0 ? "run-card-fail" : "run-card-pass"}`}
    >
      <div className="run-card-header">
        <span className="run-card-name">{run.projectName}</span>
        <TestResultBadge result={result} />
        <span className="run-card-duration">
          {result.durationSeconds.toFixed(1)}s
        </span>
      </div>

      <div className="run-card-stats">
        <span className="stat stat-passed">{result.passed} passed</span>
        {result.failed > 0 && (
          <span className="stat stat-failed">{result.failed} failed</span>
        )}
        {result.skipped > 0 && (
          <span className="stat stat-skipped">{result.skipped} skipped</span>
        )}
      </div>

      {result.failedTests.length > 0 && (
        <div className="failed-tests">
          <button
            className="expand-button"
            onClick={() => setExpanded(!expanded)}
          >
            {expanded ? "Hide" : "Show"} {result.failedTests.length} failed
            test{result.failedTests.length !== 1 ? "s" : ""}
          </button>

          {expanded &&
            result.failedTests.map((test) => (
              <div key={test.fullName} className="failed-test">
                <div className="failed-test-name">{test.fullName}</div>
                {test.errorMessage && (
                  <pre className="failed-test-error">{test.errorMessage}</pre>
                )}
                {test.stackTrace && (
                  <pre className="failed-test-stack">{test.stackTrace}</pre>
                )}
              </div>
            ))}
        </div>
      )}
    </div>
  );
}
