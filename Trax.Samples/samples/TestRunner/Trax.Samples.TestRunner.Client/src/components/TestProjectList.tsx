import { useMutation } from "@apollo/client";
import { RUN_TESTS } from "../graphql/mutations";
import { TestProject, TestRunState } from "../types";

interface TestProjectListProps {
  projects: TestProject[];
  runs: Map<string, TestRunState>;
  buildBeforeRun: boolean;
  onTestQueued: (projectName: string, externalId: string) => void;
}

export function TestProjectList({
  projects,
  runs,
  buildBeforeRun,
  onTestQueued,
}: TestProjectListProps) {
  const [runTests] = useMutation(RUN_TESTS);

  const isProjectRunning = (name: string) => {
    for (const run of runs.values()) {
      if (
        run.projectName === name &&
        (run.status === "queued" || run.status === "running")
      )
        return true;
    }
    return false;
  };

  const handleRun = async (project: TestProject) => {
    const { data } = await runTests({
      variables: {
        input: {
          projectName: project.name,
          projectPath: project.projectPath,
          build: buildBeforeRun,
        },
      },
    });

    const externalId = data?.dispatch?.runTests?.externalId;
    if (externalId) {
      onTestQueued(project.name, externalId);
    }
  };

  const handleRunAll = async () => {
    for (const project of projects) {
      if (!isProjectRunning(project.name)) {
        await handleRun(project);
      }
    }
  };

  const anyRunning = projects.some((p) => isProjectRunning(p.name));

  return (
    <div className="project-list">
      <div className="project-list-header">
        <h2>Test Projects</h2>
        <button
          className="run-all-button"
          onClick={handleRunAll}
          disabled={anyRunning}
        >
          Run All
        </button>
      </div>

      <table className="project-table">
        <thead>
          <tr>
            <th>Project</th>
            <th>Repository</th>
            <th>Requires</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {projects.map((project) => (
            <tr key={project.name}>
              <td className="project-name">{project.name}</td>
              <td className="project-repo">{project.repoName}</td>
              <td>
                {project.requiresPostgres && (
                  <span className="postgres-badge">Postgres</span>
                )}
              </td>
              <td>
                <button
                  className="run-button"
                  onClick={() => handleRun(project)}
                  disabled={isProjectRunning(project.name)}
                >
                  {isProjectRunning(project.name) ? "Running..." : "Run"}
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
