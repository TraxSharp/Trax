import { useQuery } from "urql";
import { getCurrentUser } from "../lib/auth";
import { LIST_APPLICATIONS } from "../graphql/queries";

const STATUS_COLUMNS = [
  "Drafted",
  "Sent",
  "Responded",
  "Interviewing",
  "Offered",
  "Rejected",
  "Ghosted",
];

const STATUS_COLORS: Record<string, string> = {
  Drafted: "bg-gray-100 border-gray-300",
  Sent: "bg-blue-50 border-blue-300",
  Responded: "bg-yellow-50 border-yellow-300",
  Interviewing: "bg-purple-50 border-purple-300",
  Offered: "bg-green-50 border-green-300",
  Rejected: "bg-red-50 border-red-300",
  Ghosted: "bg-gray-50 border-gray-200",
};

type Application = {
  id: string;
  jobId: string;
  status: string;
  createdAt: string;
};

export function ApplicationsPage() {
  const user = getCurrentUser();

  const [result] = useQuery({
    query: LIST_APPLICATIONS,
    variables: { input: { userId: user.id } },
  });

  const apps: Application[] =
    result.data?.discover?.listApplications?.applications || [];

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900 mb-6">Applications</h1>

      {apps.length === 0 && !result.fetching && (
        <p className="text-gray-500 text-center py-12">
          No applications yet. Add a job and create an application from the job
          detail page.
        </p>
      )}

      <div className="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-4 gap-4">
        {STATUS_COLUMNS.filter((status) =>
          apps.some((a) => a.status === status)
        ).map((status) => (
          <div key={status}>
            <h3 className="text-sm font-semibold text-gray-700 mb-2">
              {status}
            </h3>
            <div className="space-y-2">
              {apps
                .filter((a) => a.status === status)
                .map((app) => (
                  <div
                    key={app.id}
                    className={`rounded-lg border p-3 ${STATUS_COLORS[status] || "bg-white border-gray-200"}`}
                  >
                    <p className="text-xs font-mono text-gray-600 truncate">
                      {app.jobId}
                    </p>
                    <p className="text-xs text-gray-500 mt-1">
                      {new Date(app.createdAt).toLocaleDateString()}
                    </p>
                  </div>
                ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
