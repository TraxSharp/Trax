"use client";

import { useState } from "react";

export interface CodeTab {
  label: string;
  caption: string;
  blocks: { file: string; html: string }[];
}

export default function CodeTabs({ tabs }: { tabs: CodeTab[] }) {
  const [active, setActive] = useState(0);
  const tab = tabs[active];

  return (
    <div>
      <div role="tablist" className="flex flex-wrap gap-1 border-b border-border">
        {tabs.map((t, i) => (
          <button
            key={t.label}
            role="tab"
            aria-selected={i === active}
            onClick={() => setActive(i)}
            className={`-mb-px border-b-2 px-4 py-2 text-sm font-medium transition-colors ${
              i === active
                ? "border-accent text-text-primary"
                : "border-transparent text-text-muted hover:text-text-secondary"
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      <div role="tabpanel" className="pt-6">
        <p className="max-w-2xl text-sm leading-relaxed text-text-secondary">
          {tab.caption}
        </p>
        <div className="mt-4 space-y-3">
          {tab.blocks.map((block) => (
            <div
              key={block.file}
              className="overflow-hidden rounded border border-border/50"
            >
              <div className="border-b border-border/50 bg-bg-secondary px-4 py-2">
                <span className="font-mono text-xs text-text-muted">
                  {block.file}
                </span>
              </div>
              <div
                className="[&_pre]:overflow-x-auto [&_pre]:bg-bg-secondary [&_pre]:p-4 [&_pre]:font-mono [&_pre]:text-[13px] [&_pre]:leading-relaxed"
                dangerouslySetInnerHTML={{ __html: block.html }}
              />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
