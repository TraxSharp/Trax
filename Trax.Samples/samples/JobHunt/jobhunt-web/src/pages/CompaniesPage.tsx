import { useState } from "react";
import { useQuery, useMutation } from "urql";
import { getCurrentUser } from "../lib/auth";
import { LIST_WATCHED_COMPANIES, WATCH_COMPANY } from "../graphql/queries";

export function CompaniesPage() {
  const user = getCurrentUser();

  const [result, refetch] = useQuery({
    query: LIST_WATCHED_COMPANIES,
    variables: { input: { userId: user.id } },
  });

  const [, watchCompany] = useMutation(WATCH_COMPANY);

  const [name, setName] = useState("");
  const [url, setUrl] = useState("");

  const companies =
    result.data?.discover?.listWatchedCompanies?.companies || [];

  async function handleWatch(e: React.FormEvent) {
    e.preventDefault();
    await watchCompany({
      input: { userId: user.id, companyName: name, careersUrl: url },
    });
    setName("");
    setUrl("");
    refetch({ requestPolicy: "network-only" });
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900 mb-6">
        Watched Companies
      </h1>

      <form
        onSubmit={handleWatch}
        className="bg-white rounded-lg border border-gray-200 p-4 mb-6 flex gap-3"
      >
        <input
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="Company name"
          className="flex-1 border border-gray-300 rounded-lg px-3 py-2 text-sm"
          required
        />
        <input
          type="url"
          value={url}
          onChange={(e) => setUrl(e.target.value)}
          placeholder="Careers page URL"
          className="flex-1 border border-gray-300 rounded-lg px-3 py-2 text-sm"
          required
        />
        <button
          type="submit"
          className="px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700"
        >
          Watch
        </button>
      </form>

      {companies.length === 0 && !result.fetching && (
        <p className="text-gray-500 text-center py-12">
          No companies watched yet.
        </p>
      )}

      <div className="bg-white rounded-lg border border-gray-200 divide-y divide-gray-100">
        {companies.map(
          (c: {
            id: string;
            companyName: string;
            careersUrl: string;
            lastCheckedAt: string | null;
          }) => (
            <div key={c.id} className="px-4 py-4">
              <div className="flex items-center justify-between">
                <span className="font-medium text-gray-900">
                  {c.companyName}
                </span>
                {c.lastCheckedAt && (
                  <span className="text-xs text-gray-500">
                    Checked:{" "}
                    {new Date(c.lastCheckedAt).toLocaleDateString()}
                  </span>
                )}
              </div>
              <p className="text-xs text-gray-400 mt-1 truncate">
                {c.careersUrl}
              </p>
            </div>
          )
        )}
      </div>
    </div>
  );
}
