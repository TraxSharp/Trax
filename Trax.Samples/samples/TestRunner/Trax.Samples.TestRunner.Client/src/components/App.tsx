import { useCallback, useState } from "react";
import { useQuery, useSubscription } from "@apollo/client";
import { DISCOVER_TEST_PROJECTS } from "../graphql/queries";
import {
  ON_TRAIN_COMPLETED,
  ON_TRAIN_FAILED,
} from "../graphql/subscriptions";
import { TestProject, TestRunState, TestResult } from "../types";
import { TestProjectList } from "./TestProjectList";
import { TestRunCard } from "./TestRunCard";

const RUN_TESTS_TRAIN_NAME =
  "Trax.Samples.TestRunner.Trains.RunTests.IRunTestsTrain";

export function App() {
  const { data, loading, error } = useQuery(DISCOVER_TEST_PROJECTS);
  const [runs, setRuns] = useState<Map<string, TestRunState>>(new Map());
  const [buildBeforeRun, setBuildBeforeRun] = useState(true);

  const handleTestQueued = useCallback(
    (projectName: string, externalId: string) => {
      setRuns((prev) => {
        const next = new Map(prev);
        next.set(externalId, { projectName, externalId, status: "queued" });
        return next;
      });
    },
    [],
  );

  useSubscription(ON_TRAIN_COMPLETED, {
    onData: ({ data: subData }) => {
      const event = subData?.data?.onTrainCompleted;
      if (!event || event.trainName !== RUN_TESTS_TRAIN_NAME) return;

      const { externalId, output } = event;

      let result: TestResult | undefined;
      if (output) {
        try {
          // output is either a parsed object (AnyType) or a JSON string
          const parsed =
            typeof output === "string" ? JSON.parse(output) : output;
          const raw = parsed.result ?? parsed.Result ?? parsed;
          // .NET JSON reference tracking wraps arrays as { $values: [...] }
          const failedTests = raw.failedTests?.$values ?? raw.failedTests ?? [];
          result = { ...raw, failedTests };
        } catch {
          // output wasn't valid JSON
        }
      }

      setRuns((prev) => {
        const next = new Map(prev);
        const existing = next.get(externalId);
        if (existing) {
          next.set(externalId, { ...existing, status: "completed", result });
        }
        return next;
      });
    },
  });

  useSubscription(ON_TRAIN_FAILED, {
    onData: ({ data: subData }) => {
      const event = subData?.data?.onTrainFailed;
      if (!event || event.trainName !== RUN_TESTS_TRAIN_NAME) return;

      const { externalId, failureReason } = event;

      setRuns((prev) => {
        const next = new Map(prev);
        const existing = next.get(externalId);
        if (existing) {
          next.set(externalId, {
            ...existing,
            status: "failed",
            failureReason,
          });
        }
        return next;
      });
    },
  });

  const projects: TestProject[] =
    data?.discover?.discoverTestProjects?.projects ?? [];

  const runEntries = Array.from(runs.values()).reverse();

  return (
    <div className="app">
      <header className="app-header">
        <h1>Trax Test Runner</h1>
        <p className="app-subtitle">
          Run NUnit tests across the Trax monorepo
        </p>
        <label className="build-toggle">
          <input
            type="checkbox"
            checked={buildBeforeRun}
            onChange={(e) => setBuildBeforeRun(e.target.checked)}
          />
          Build before run
        </label>
      </header>

      <main className="app-main">
        {loading && <p className="loading">Loading test projects...</p>}
        {error && <p className="error">Error: {error.message}</p>}

        {!loading && !error && (
          <TestProjectList
            projects={projects}
            runs={runs}
            buildBeforeRun={buildBeforeRun}
            onTestQueued={handleTestQueued}
          />
        )}

        {runEntries.length > 0 && (
          <div className="results-section">
            <h2>Results</h2>
            <div className="results-list">
              {runEntries.map((run) => (
                <TestRunCard key={run.externalId} run={run} />
              ))}
            </div>
          </div>
        )}
      </main>
    </div>
  );
}
