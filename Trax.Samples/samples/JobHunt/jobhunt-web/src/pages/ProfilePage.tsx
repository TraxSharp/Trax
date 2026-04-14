import { useState, useEffect } from "react";
import { useQuery, useMutation } from "urql";
import { getCurrentUser } from "../lib/auth";
import { GET_PROFILE, UPDATE_PROFILE } from "../graphql/queries";

const FACETS = ["Skills", "Education", "WorkHistory"] as const;
type Facet = (typeof FACETS)[number];

const FACET_MAP: Record<Facet, { key: string; graphql: string }> = {
  Skills: { key: "skillsJson", graphql: "SKILLS" },
  Education: { key: "educationJson", graphql: "EDUCATION" },
  WorkHistory: { key: "workHistoryJson", graphql: "WORK_HISTORY" },
};

export function ProfilePage() {
  const user = getCurrentUser();
  const [activeFacet, setActiveFacet] = useState<Facet>("Skills");
  const [editValue, setEditValue] = useState("");
  const [saved, setSaved] = useState(false);

  const [profileResult, refetch] = useQuery({
    query: GET_PROFILE,
    variables: { input: { userId: user.id } },
  });

  const [, updateProfile] = useMutation(UPDATE_PROFILE);

  const profile = profileResult.data?.discover?.getProfile;

  useEffect(() => {
    if (profile) {
      const key = FACET_MAP[activeFacet].key;
      const raw = profile[key] || "[]";
      try {
        setEditValue(JSON.stringify(JSON.parse(raw), null, 2));
      } catch {
        setEditValue(raw);
      }
    }
  }, [profile, activeFacet]);

  async function handleSave() {
    try {
      JSON.parse(editValue);
    } catch {
      alert("Invalid JSON");
      return;
    }

    await updateProfile({
      input: {
        userId: user.id,
        facet: FACET_MAP[activeFacet].graphql,
        json: editValue,
      },
    });
    refetch({ requestPolicy: "network-only" });
    setSaved(true);
    setTimeout(() => setSaved(false), 2000);
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900 mb-6">Profile</h1>

      <div className="flex gap-2 mb-4">
        {FACETS.map((f) => (
          <button
            key={f}
            onClick={() => setActiveFacet(f)}
            className={`px-4 py-2 text-sm rounded-lg ${
              activeFacet === f
                ? "bg-blue-600 text-white"
                : "border border-gray-300 text-gray-700 hover:bg-gray-50"
            }`}
          >
            {f}
          </button>
        ))}
      </div>

      <div className="bg-white rounded-lg border border-gray-200 p-6">
        <label className="block text-sm font-medium text-gray-700 mb-2">
          {activeFacet} (JSON array)
        </label>
        <textarea
          value={editValue}
          onChange={(e) => setEditValue(e.target.value)}
          rows={12}
          className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm font-mono"
        />
        <div className="flex items-center gap-3 mt-3">
          <button
            onClick={handleSave}
            className="px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700"
          >
            Save
          </button>
          {saved && (
            <span className="text-sm text-green-600">Saved</span>
          )}
        </div>
      </div>
    </div>
  );
}
