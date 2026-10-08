import { useEffect, useState } from "react";
import { useTopicMapDraft } from "../topicMap/useTopicMapDraft";

// The corpus's fields and the years a map may cover, as the host's CorpusFixture holds them.
const FIELDS = ["Hydrology", "Soil science", "Urban ecology"];
const YEARS = Array.from({ length: 10 }, (_, i) => 2016 + i);

const STEP_LABEL: Record<string, string> = {
  ChoosingFields: "1. Fields",
  ChoosingRange: "2. Years",
  Building: "3. Building",
  Built: "Built",
  BuildFailed: "Build failed",
  BuildCancelled: "Build cancelled",
};

/**
 * "Build my topic map": the topic-map state machine's wizard. Each step is a trigger the generated
 * twin checks and the server applies; Building runs the topic map train on the server, and only its
 * outcome moves the draft on. The draft is stored per demo key, so a reload comes back to the same step.
 */
export function TopicMapWizard() {
  const draft = useTopicMapDraft();
  const { snapshot, busy } = draft;
  const context = snapshot?.context ?? {};
  const [fields, setFields] = useState<string[]>(FIELDS);
  const [fromYear, setFromYear] = useState(2016);
  const [toYear, setToYear] = useState(2025);

  // A loaded draft brings its own choices back.
  useEffect(() => {
    if (context.fields) setFields(context.fields);
    if (context.fromYear != null) setFromYear(context.fromYear);
    if (context.toYear != null) setToYear(context.toYear);
  }, [context.fields, context.fromYear, context.toYear]);

  if (!snapshot) return <p className="hint">{draft.problem ?? "Loading your draft…"}</p>;

  const state = snapshot.state;
  const toggle = (field: string) =>
    setFields((now) => (now.includes(field) ? now.filter((f) => f !== field) : [...now, field]));
  const button = (label: string, act: () => void, enabled: boolean, primary = false) => (
    <button className={primary ? "primary" : "action"} onClick={act} disabled={busy || !enabled}>
      {label}
    </button>
  );

  return (
    <>
      <p className="wizard-step">
        <span className={`wizard-state ${state}`}>{STEP_LABEL[state] ?? state}</span>
        <code>draft {draft.id.slice(0, 8)}</code>
      </p>

      {state === "ChoosingFields" && (
        <>
          {FIELDS.map((f) => (
            <label key={f} className="tick">
              <input type="checkbox" checked={fields.includes(f)} onChange={() => toggle(f)} />
              {f}
            </label>
          ))}
          {button("Next", () => void draft.advance("ChooseFields", { fields }), draft.can("ChooseFields", { fields }), true)}
        </>
      )}

      {state === "ChoosingRange" && (
        <>
          <div className="action-pair">
            <YearPicker label="From" value={fromYear} set={setFromYear} />
            <YearPicker label="To" value={toYear} set={setToYear} />
          </div>
          {button("Build the map", () => void draft.advance("Build", { fromYear, toYear }), draft.can("Build", { fromYear, toYear }), true)}
          {button("Back", () => void draft.advance("Back"), draft.can("Back"))}
        </>
      )}

      {state === "Building" && (
        <>
          <p className="hint">
            <span className="signal">The server is running BuildTopicMapTrain for this draft. Only its outcome moves it on.</span>
          </p>
          {button("Cancel the build", () => void draft.advance("CancelBuild"), draft.can("CancelBuild"))}
        </>
      )}

      {state === "Built" && (
        <p className="hint">
          {context.papers} papers, {context.topicPairs} topic pairs, co-citation {context.coCitationTrack}. The pairs are
          stored under <code>{context.mapId}</code>; the draft keeps only that pointer.
        </p>
      )}
      {state === "BuildFailed" && (
        <p className="hint">The build failed: a slice needs at least two papers, or the run's host went away.</p>
      )}
      {state === "BuildCancelled" && <p className="hint">The build's run was cancelled.</p>}

      {(state === "Built" || state === "BuildFailed" || state === "BuildCancelled") && (
        <div className="action-pair">
          {button("Rebuild", () => void draft.advance("Rebuild"), draft.can("Rebuild"))}
          {button("Edit", () => void draft.advance("Edit"), draft.can("Edit"))}
        </div>
      )}

      {draft.problem && <p className="hint error">{draft.problem}</p>}
      <button className="action" onClick={draft.startOver} disabled={busy}>
        Start a new map
      </button>
    </>
  );
}

function YearPicker({ label, value, set }: { label: string; value: number; set: (year: number) => void }) {
  return (
    <label className="field">
      {label}
      <select value={value} onChange={(e) => set(Number(e.target.value))}>
        {[1990, ...YEARS].map((y) => (
          <option key={y} value={y}>
            {y}
          </option>
        ))}
      </select>
    </label>
  );
}
