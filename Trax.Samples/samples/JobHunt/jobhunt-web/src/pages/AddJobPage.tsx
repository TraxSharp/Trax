import { useState } from "react";
import { useMutation } from "urql";
import { useNavigate } from "react-router-dom";
import { getCurrentUser } from "../lib/auth";
import { ADD_JOB } from "../graphql/queries";

export function AddJobPage() {
  const user = getCurrentUser();
  const navigate = useNavigate();
  const [, addJob] = useMutation(ADD_JOB);

  const [mode, setMode] = useState<"url" | "paste">("paste");
  const [url, setUrl] = useState("");
  const [title, setTitle] = useState("");
  const [company, setCompany] = useState("");
  const [description, setDescription] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);

    const input =
      mode === "url"
        ? { userId: user.id, url }
        : {
            userId: user.id,
            pastedTitle: title,
            pastedCompany: company,
            pastedDescription: description,
          };

    const result = await addJob({ input });

    if (result.error) {
      setError(result.error.message);
      setSubmitting(false);
      return;
    }

    const jobId = result.data?.dispatch?.addJob?.output?.jobId;
    if (jobId) {
      navigate(`/jobs/${jobId}`);
    } else {
      navigate("/jobs");
    }
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900 mb-6">Add Job</h1>

      <div className="mb-6 flex gap-2">
        <button
          onClick={() => setMode("paste")}
          className={`px-4 py-2 text-sm rounded-lg ${
            mode === "paste"
              ? "bg-blue-600 text-white"
              : "border border-gray-300 text-gray-700 hover:bg-gray-50"
          }`}
        >
          Paste Details
        </button>
        <button
          onClick={() => setMode("url")}
          className={`px-4 py-2 text-sm rounded-lg ${
            mode === "url"
              ? "bg-blue-600 text-white"
              : "border border-gray-300 text-gray-700 hover:bg-gray-50"
          }`}
        >
          From URL
        </button>
      </div>

      <form
        onSubmit={handleSubmit}
        className="bg-white rounded-lg border border-gray-200 p-6 space-y-4"
      >
        {mode === "url" ? (
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Job Posting URL
            </label>
            <input
              type="url"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              placeholder="https://boards.greenhouse.io/company/jobs/12345"
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
              required
            />
            <p className="text-xs text-gray-500 mt-1">
              The scraper will extract the job title, company, and description.
            </p>
          </div>
        ) : (
          <>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Job Title
              </label>
              <input
                type="text"
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                placeholder="Senior Software Engineer"
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
                required
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Company
              </label>
              <input
                type="text"
                value={company}
                onChange={(e) => setCompany(e.target.value)}
                placeholder="Acme Corp"
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
                required
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Description
              </label>
              <textarea
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="Paste the job description here..."
                rows={8}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
                required
              />
            </div>
          </>
        )}

        {error && (
          <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded-lg text-sm">
            {error}
          </div>
        )}

        <button
          type="submit"
          disabled={submitting}
          className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50"
        >
          {submitting ? "Adding..." : "Add Job"}
        </button>
      </form>
    </div>
  );
}
