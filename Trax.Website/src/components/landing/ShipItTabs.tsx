"use client";

import dynamic from "next/dynamic";
import { useState } from "react";
import WhatYouWrite from "./WhatYouWrite";

// The second and third tabs read the deploy recordings, which are too big to load with the page; each loads when its
// tab is first opened.
const loading = () => <p className="text-sm text-text-muted">Loading the recording…</p>;
const ReleaseStory = dynamic(() => import("./ReleaseStory"), { loading });
const ReachMap = dynamic(() => import("./ReachMap"), { loading });

// The deploy section's three tabs: what trax writes from your file, one release as it ran, and who can reach whom
// once it is deployed. The first is open when the page loads; a tab is only drawn while it is open, so each one
// starts its own motion when it is shown.
const TABS = [
  { key: "write", label: "What you write, what Trax writes", body: () => <WhatYouWrite /> },
  { key: "release", label: "A release", body: () => <ReleaseStory /> },
  { key: "reach", label: "Who can talk to whom", body: () => <ReachMap /> },
];

export default function ShipItTabs() {
  const [open, setOpen] = useState(TABS[0].key);
  const tab = TABS.find((t) => t.key === open) ?? TABS[0];
  return (
    <div className="flex flex-col gap-4">
      <div role="tablist" aria-label="Deploying with trax" className="flex flex-wrap gap-1.5 border-b border-border">
        {TABS.map((t) => (
          <button
            key={t.key}
            role="tab"
            id={`ship-tab-${t.key}`}
            aria-selected={t.key === open}
            aria-controls={`ship-panel-${t.key}`}
            onClick={() => setOpen(t.key)}
            className={`-mb-px rounded-t-md border px-3.5 py-2 text-sm transition-colors ${
              t.key === open ? "border-border border-b-bg-primary bg-bg-primary font-medium text-text-primary" : "border-transparent text-text-secondary hover:text-text-primary"
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>
      <div role="tabpanel" id={`ship-panel-${tab.key}`} aria-labelledby={`ship-tab-${tab.key}`}>
        {tab.body()}
      </div>
    </div>
  );
}
