import { useQuery } from "urql";
import { getCurrentUser } from "../lib/auth";
import { LIST_JOBS } from "../graphql/queries";
import { Link } from "react-router-dom";

export function JobsPage() {
  const user = getCurrentUser();

  const [result, reexecute] = useQuery({
    query: LIST_JOBS,
    variables: { input: { userId: user.id } },
  });

  const jobs = result.data?.discover?.listJobs?.jobs || [];

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Jobs</h1>
        <div className="flex gap-2">
          <button
            onClick={() => reexecute({ requestPolicy: "network-only" })}
            className="px-3 py-2 text-sm border border-gray-300 rounded-lg hover:bg-gray-50"
          >
            Refresh
          </button>
          <Link
            to="/jobs/new"
            className="px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700"
          >
            Add Job
          </Link>
        </div>
      </div>

      {result.fetching && <p className="text-gray-500">Loading...</p>}

      {jobs.length === 0 && !result.fetching && (
        <p className="text-gray-500 text-center py-12">
          No jobs yet. Add one to get started.
        </p>
      )}

      <div className="bg-white rounded-lg border border-gray-200 divide-y divide-gray-100">
        {jobs.map(
          (job: {
            id: string;
            title: string;
            company: string;
            url: string | null;
            status: string;
            createdAt: string;
          }) => (
            <Link
              key={job.id}
              to={`/jobs/${job.id}`}
              className="block px-4 py-4 hover:bg-gray-50 transition-colors"
            >
              <div className="flex items-center justify-between">
                <div>
                  <span className="font-medium text-gray-900">{job.title}</span>
                  <span className="text-gray-500 ml-2">at {job.company}</span>
                </div>
                <span
                  className={`text-xs px-2 py-1 rounded-full ${
                    job.status === "Active"
                      ? "bg-green-100 text-green-700"
                      : job.status === "Closed"
                        ? "bg-red-100 text-red-700"
                        : "bg-gray-100 text-gray-600"
                  }`}
                >
                  {job.status}
                </span>
              </div>
              {job.url && (
                <p className="text-xs text-gray-400 mt-1 truncate">{job.url}</p>
              )}
            </Link>
          )
        )}
      </div>
    </div>
  );
}
