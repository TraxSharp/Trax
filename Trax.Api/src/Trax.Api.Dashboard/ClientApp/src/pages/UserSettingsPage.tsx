import { useState, useSyncExternalStore } from "react";
import { getTheme, setTheme, subscribeTheme, type Theme } from "../lib/theme";
import { setPollSeconds, usePollSeconds } from "../lib/poll";
import { setHideAdminTrains, useHideAdminTrains } from "../lib/adminTrains";
import { clearCredential, getAuthMode, getCredential } from "../lib/auth";
import {
  notificationsSupported,
  notifyEnabled,
  requestNotifyPermission,
  setNotifyEnabled,
} from "../lib/notify";
import { toast } from "../lib/toast";
import {
  OVERVIEW_PANELS,
  resetOverviewPanels,
  setOverviewPanel,
  useOverviewPanels,
} from "../lib/overviewPanels";
import { Bell, BellOff, Moon, Sun } from "lucide-react";

export function UserSettingsPage() {
  const theme = useSyncExternalStore(subscribeTheme, getTheme, () => "light");
  const key = getCredential();
  const mode = getAuthMode();
  const masked =
    key.length > 8 ? `${key.slice(0, 4)}…${key.slice(-4)}` : "•".repeat(key.length);

  return (
    <div>
      <h1 className="text-2xl font-bold text-fg mb-6">
        User settings
      </h1>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Panel title="Appearance">
          <p className="text-xs text-muted mb-2">Theme</p>
          <div className="flex gap-2">
            {(["light", "dark"] as Theme[]).map((t) => (
              <button
                key={t}
                onClick={() => setTheme(t)}
                className={`inline-flex items-center gap-2 px-4 py-2 rounded-lg text-sm border ${
                  theme === t
                    ? "bg-accent-soft border-accent-line text-accent-fg"
                    : "border-line-strong text-fg-2 hover:bg-hover"
                }`}
              >
                {t === "light" ? <Sun className="size-4" /> : <Moon className="size-4" />}
                {t === "light" ? "Light" : "Dark"}
              </button>
            ))}
          </div>
        </Panel>

        <Panel title="Connection">
          <dl className="grid grid-cols-2 gap-y-2 text-sm">
            <dt className="text-muted">Auth mode</dt>
            <dd className="text-fg text-right">
              {mode === "bearer" ? "Bearer token (JWT)" : "API key"}
            </dd>
            <dt className="text-muted">Credential</dt>
            <dd className="text-fg font-mono text-right">
              {masked || "—"}
            </dd>
          </dl>
          <button
            onClick={clearCredential}
            className="mt-4 px-4 py-2 rounded-lg text-sm border border-line-strong text-fg-2 hover:bg-hover"
          >
            Change credential
          </button>
        </Panel>

        <Panel title="Notifications">
          <NotificationsToggle />
        </Panel>

        <Panel title="Auto-refresh">
          <AutoRefreshControl />
        </Panel>

        <Panel title="Administration trains">
          <AdminTrainsToggle />
        </Panel>

        <Panel title="Overview panels">
          <OverviewPanelToggles />
        </Panel>
      </div>
    </div>
  );
}

// Which sections the Overview page shows. Each switch applies at once and is remembered in this
// browser; Reset default shows every panel again.
function OverviewPanelToggles() {
  const panels = useOverviewPanels();
  return (
    <div>
      <p className="text-sm text-fg-2 mb-3">
        Toggle which sections are visible on the Overview page.
      </p>
      <div className="space-y-3">
        {OVERVIEW_PANELS.map((p) => (
          <label key={p.key} className="flex items-start gap-2 text-sm text-fg-2">
            <input
              type="checkbox"
              role="switch"
              aria-label={`Show ${p.label}`}
              checked={panels[p.key]}
              onChange={(e) => setOverviewPanel(p.key, e.target.checked)}
              className="mt-1"
            />
            <span>
              {p.label}
              <span className="block text-xs text-muted">{p.description}</span>
            </span>
          </label>
        ))}
      </div>
      <button
        onClick={resetOverviewPanels}
        className="mt-4 px-4 py-2 rounded-lg text-sm border border-line-strong text-fg-2 hover:bg-hover"
      >
        Reset default
      </button>
    </div>
  );
}

function AdminTrainsToggle() {
  const hide = useHideAdminTrains();
  return (
    <div>
      <p className="text-sm text-fg-2 mb-3">
        The internal scheduler trains (JobDispatcher, ManifestManager, JobRunner, cleanup) run
        constantly. Hide them from the Executions grid, its live feed, and the Overview metrics so
        you see your own trains instead of the plumbing.
      </p>
      <label className="flex items-center gap-2 text-sm text-fg-2">
        <input
          type="checkbox"
          aria-label="Hide administration trains"
          checked={hide}
          onChange={(e) => setHideAdminTrains(e.target.checked)}
        />
        Hide administration trains
      </label>
    </div>
  );
}

function AutoRefreshControl() {
  const seconds = usePollSeconds();
  return (
    <div>
      <p className="text-sm text-fg-2 mb-3">
        The list pages update live over the subscription when data changes, so you normally don't
        need this. It's a fallback that re-queries on a fixed interval in case the WebSocket drops. 0
        (the default) turns it off.
      </p>
      <label className="flex items-center gap-2 text-sm text-fg-2">
        Every
        <input
          type="number"
          min={0}
          aria-label="Auto-refresh seconds"
          value={seconds}
          onChange={(e) => setPollSeconds(Number(e.target.value))}
          className="w-20 border border-line-strong rounded-md px-2 py-1"
        />
        seconds
      </label>
    </div>
  );
}

function NotificationsToggle() {
  const supported = notificationsSupported();
  const [enabled, setEnabled] = useState(notifyEnabled());

  async function toggle() {
    if (enabled) {
      setNotifyEnabled(false);
      setEnabled(false);
      toast("Failure notifications off.", "info");
      return;
    }
    const perm = await requestNotifyPermission();
    if (perm !== "granted") {
      toast("Browser denied notification permission.", "error");
      return;
    }
    setNotifyEnabled(true);
    setEnabled(true);
    toast("Failure notifications on.", "success");
  }

  if (!supported)
    return (
      <p className="text-sm text-muted">
        This browser doesn't support notifications.
      </p>
    );

  return (
    <div>
      <p className="text-sm text-fg-2 mb-3">
        Get a desktop notification whenever a train fails.
      </p>
      <button
        onClick={toggle}
        className={`inline-flex items-center gap-2 px-4 py-2 rounded-lg text-sm border ${
          enabled
            ? "bg-accent-soft border-accent-line text-accent-fg"
            : "border-line-strong text-fg-2 hover:bg-hover"
        }`}
      >
        {enabled ? <Bell className="size-4" /> : <BellOff className="size-4" />}
        {enabled ? "Enabled" : "Enable notifications"}
      </button>
    </div>
  );
}

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="bg-surface rounded-lg border border-line p-5">
      <h2 className="text-sm font-semibold text-fg mb-3">
        {title}
      </h2>
      {children}
    </div>
  );
}
