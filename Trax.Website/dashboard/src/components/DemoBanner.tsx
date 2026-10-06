import { Info } from "lucide-react";

// Says, on every page of the demo build, that nothing here is a live system.
export function DemoBanner() {
  return (
    <div
      role="note"
      className="shrink-0 flex items-center justify-center gap-2 px-4 py-1.5 text-xs bg-warn-soft text-warn-fg border-b border-warn-line"
    >
      <Info className="size-3.5 shrink-0" />
      <span>
        <strong className="font-semibold">Demo</strong> — recorded data, changes are simulated. Every page answers
        from recordings of real Trax sample hosts; what you change stays in this tab until you reload.
      </span>
    </div>
  );
}
