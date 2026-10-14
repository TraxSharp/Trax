import { useConnectionStatus } from "../lib/useConnection";
import type { ConnectionStatus } from "../lib/graphql";

const MAP: Record<ConnectionStatus, { dot: string; label: string; ping: boolean }> = {
  connected: { dot: "bg-ok", label: "Live", ping: true },
  connecting: { dot: "bg-warn", label: "Connecting", ping: false },
  closed: { dot: "bg-idle", label: "Offline", ping: false },
};

export function ConnectionIndicator() {
  const status = useConnectionStatus();
  const { dot, label, ping } = MAP[status];
  return (
    <div className="flex items-center gap-2 text-xs text-muted">
      <span className="relative flex h-2 w-2">
        {ping && (
          <span
            className={`absolute inline-flex h-full w-full rounded-full ${dot} opacity-75 animate-ping`}
          />
        )}
        <span className={`relative inline-flex h-2 w-2 rounded-full ${dot}`} />
      </span>
      {label}
    </div>
  );
}
