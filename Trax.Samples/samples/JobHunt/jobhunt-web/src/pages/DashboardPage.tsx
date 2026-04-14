import { useQuery } from "urql";
import { getCurrentUser } from "../lib/auth";
import { LIST_JOBS, LIST_APPLICATIONS, LIST_WATCHED_COMPANIES } from "../graphql/queries";
import { Link } from "react-router-dom";

export function DashboardPage() {
  const user = getCurrentUser();

  const [jobsResult] = useQuery({
    query: LIST_JOBS,
    variables: { input: { userId: user.id } },
  });

  const [appsResult] = useQuery({
    query: LIST_APPLICATIONS,
    variables: { input: { userId: user.id } },
  });

  const [companiesResult] = useQuery({
    query: LIST_WATCHED_COMPANIES,
    variables: { input: { userId: user.id } },
  });

  const jobs = jobsResult.data?.discover?.listJobs?.jobs || [];
  const apps = appsResult.data?.discover?.listApplications?.applications || [];
  const companies =
    companiesResult.data?.discover?.listWatchedCompanies?.companies || [];

  const activeJobs = jobs.filter(
    (j: { status: string }) => j.status === "Active"
  );

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900 mb-6">
        Welcome back, {user.name}
      </h1>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
        <StatCard
          label="Active Jobs"
          value={activeJobs.length}
          link="/jobs"
        />
        <StatCard
          label="Applications"
          value={apps.length}
          link="/applications"
        />
        <StatCard
          label="Watched Companies"
          value={companies.length}
          link="/companies"
        />
      </div>

      {jobs.length > 0 && (
        <div>
          <h2 className="text-lg font-semibold text-gray-900 mb-3">
            Recent Jobs
          </h2>
          <div className="bg-white rounded-lg border border-gray-200 divide-y divide-gray-100">
            {jobs.slice(0, 5).map((job: { id: string; title: string; company: string; status: string }) => (
              <div
                key={job.id}
                className="px-4 py-3 flex items-center justify-between"
              >
                <div>
                  <span className="font-medium text-gray-900">
                    {job.title}
                  </span>
                  <span className="text-gray-500 ml-2">at {job.company}</span>
                </div>
                <span
                  className={`text-xs px-2 py-1 rounded-full ${
                    job.status === "Active"
                      ? "bg-green-100 text-green-700"
                      : "bg-gray-100 text-gray-600"
                  }`}
                >
                  {job.status}
                </span>
              </div>
            ))}
          </div>
        </div>
      )}

      {jobs.length === 0 && (
        <div className="text-center py-12 text-gray-500">
          <p className="mb-4">No jobs tracked yet.</p>
          <Link
            to="/jobs/new"
            className="inline-flex px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700"
          >
            Add your first job
          </Link>
        </div>
      )}
    </div>
  );
}

function StatCard({
  label,
  value,
  link,
}: {
  label: string;
  value: number;
  link: string;
}) {
  return (
    <Link
      to={link}
      className="bg-white rounded-lg border border-gray-200 p-6 hover:border-blue-300 transition-colors"
    >
      <p className="text-sm text-gray-500">{label}</p>
      <p className="text-3xl font-bold text-gray-900 mt-1">{value}</p>
    </Link>
  );
}
