import { useState } from "react";
import { FoodOrder } from "./FoodOrder";
import { Workflows } from "./Workflows";

const VIEWS = [
  { key: "order", label: "Food order" },
  { key: "workflows", label: "Workflows" },
] as const;

type View = (typeof VIEWS)[number]["key"];

/** The page: the food order (a state machine that runs a train) and the workflows (trains that crash and recover). */
export function App() {
  const [view, setView] = useState<View>("order");
  return (
    <div className="page">
      <header className="page-head">
        <span className="eyebrow">
          <span className="mark">T</span>
          Trax · Recovery
        </span>
        <div role="tablist" className="tabs" aria-label="Demo">
          {VIEWS.map((v) => (
            <button key={v.key} role="tab" aria-selected={view === v.key} className={view === v.key ? "active" : ""} onClick={() => setView(v.key)}>
              {v.label}
            </button>
          ))}
        </div>
        <a className="dashboard-link" href="http://localhost:5260/trax" target="_blank" rel="noreferrer">
          Open the Trax dashboard ↗
        </a>
      </header>
      {/* Both stay mounted, so switching back finds a run or an order where it was. */}
      <div role="tabpanel" hidden={view !== "order"}>
        <FoodOrder />
      </div>
      <div role="tabpanel" hidden={view !== "workflows"}>
        <Workflows />
      </div>
    </div>
  );
}
