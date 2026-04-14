import { useState } from "react";
import { useParams } from "react-router-dom";
import { useQuery, useMutation } from "urql";
import { getCurrentUser } from "../lib/auth";
import {
  GET_ARTIFACTS,
  GENERATE_MATERIALS,
  FIND_CONTACT,
  CREATE_APPLICATION,
} from "../graphql/queries";

export function JobDetailPage() {
  const { jobId } = useParams<{ jobId: string }>();
  const user = getCurrentUser();

  const [artifactsResult, refetchArtifacts] = useQuery({
    query: GET_ARTIFACTS,
    variables: { input: { jobId, userId: user.id } },
  });

  const [, generateMaterials] = useMutation(GENERATE_MATERIALS);
  const [, findContact] = useMutation(FIND_CONTACT);
  const [, createApplication] = useMutation(CREATE_APPLICATION);

  const [generating, setGenerating] = useState(false);
  const [contactName, setContactName] = useState("");
  const [contactEmail, setContactEmail] = useState("");

  const artifacts =
    artifactsResult.data?.discover?.getArtifacts?.artifacts || [];
  const resume = artifacts.find(
    (a: { type: string }) => a.type === "Resume"
  );
  const coverLetter = artifacts.find(
    (a: { type: string }) => a.type === "CoverLetter"
  );

  async function handleGenerate() {
    setGenerating(true);
    await generateMaterials({
      input: { jobId, userId: user.id },
    });
    refetchArtifacts({ requestPolicy: "network-only" });
    setGenerating(false);
  }

  async function handleAddContact(e: React.FormEvent) {
    e.preventDefault();
    await findContact({
      input: { jobId, name: contactName, email: contactEmail },
    });
    setContactName("");
    setContactEmail("");
  }

  async function handleCreateApplication() {
    await createApplication({
      input: { jobId, userId: user.id },
    });
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900 mb-2">Job Detail</h1>
      <p className="text-sm text-gray-500 mb-6 font-mono">{jobId}</p>

      <div className="space-y-6">
        {/* Actions */}
        <div className="flex gap-3">
          <button
            onClick={handleGenerate}
            disabled={generating}
            className="px-4 py-2 text-sm bg-purple-600 text-white rounded-lg hover:bg-purple-700 disabled:opacity-50"
          >
            {generating ? "Generating..." : "Generate Resume + Cover Letter"}
          </button>
          <button
            onClick={handleCreateApplication}
            className="px-4 py-2 text-sm border border-gray-300 rounded-lg hover:bg-gray-50"
          >
            Create Application
          </button>
        </div>

        {/* Generated Materials */}
        {(resume || coverLetter) && (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {resume && (
              <div className="bg-white rounded-lg border border-gray-200 p-4">
                <div className="flex items-center justify-between mb-3">
                  <h3 className="font-semibold text-gray-900">Resume</h3>
                  <span className="text-xs text-gray-500">
                    {resume.modelUsed}
                  </span>
                </div>
                <pre className="text-xs text-gray-700 whitespace-pre-wrap max-h-96 overflow-auto bg-gray-50 p-3 rounded">
                  {resume.content}
                </pre>
              </div>
            )}
            {coverLetter && (
              <div className="bg-white rounded-lg border border-gray-200 p-4">
                <div className="flex items-center justify-between mb-3">
                  <h3 className="font-semibold text-gray-900">Cover Letter</h3>
                  <span className="text-xs text-gray-500">
                    {coverLetter.modelUsed}
                  </span>
                </div>
                <pre className="text-xs text-gray-700 whitespace-pre-wrap max-h-96 overflow-auto bg-gray-50 p-3 rounded">
                  {coverLetter.content}
                </pre>
              </div>
            )}
          </div>
        )}

        {/* Add Contact */}
        <div className="bg-white rounded-lg border border-gray-200 p-4">
          <h3 className="font-semibold text-gray-900 mb-3">Add Contact</h3>
          <form onSubmit={handleAddContact} className="flex gap-3">
            <input
              type="text"
              value={contactName}
              onChange={(e) => setContactName(e.target.value)}
              placeholder="Name"
              className="flex-1 border border-gray-300 rounded-lg px-3 py-2 text-sm"
              required
            />
            <input
              type="email"
              value={contactEmail}
              onChange={(e) => setContactEmail(e.target.value)}
              placeholder="Email"
              className="flex-1 border border-gray-300 rounded-lg px-3 py-2 text-sm"
              required
            />
            <button
              type="submit"
              className="px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700"
            >
              Save
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}
