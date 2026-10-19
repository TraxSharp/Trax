import { useState } from "react";
import { Link, Outlet, useLocation } from "react-router-dom";
import {
  CalendarClock,
  ChevronDown,
  ChevronRight,
  FileCog,
  FlaskConical,
  Inbox,
  KeyRound,
  Layers,
  ListChecks,
  LayoutDashboard,
  MailX,
  Network,
  Pin,
  Play,
  Radio,
  ScrollText,
  Server,
  Settings,
  Sparkles,
  TrainFront,
  User,
  Workflow,
  type LucideIcon,
} from "lucide-react";
import { useAnswers } from "../lib/answerable";
import { clearCredential, hostAuthenticates } from "../lib/auth";
import { getGlobalMockStore } from "../mock/global-store";
import { ConnectionIndicator } from "./ConnectionIndicator";
import { DemoBanner } from "./DemoBanner";
import { isDemo } from "../lib/demo";
import { DashboardHeader } from "./DashboardHeader";
import { DashboardFooter } from "./DashboardFooter";
import { usePersistedOperationsAvailable } from "../lib/persistedOperations";
import { GlobalProgressBar } from "./GlobalProgressBar";
import { NotificationBridge } from "./NotificationBridge";
import { ThemeToggle } from "./ThemeToggle";
import { ToastHost } from "./ToastHost";
import { ConfirmHost } from "./ConfirmHost";

interface NavItem {
  path: string;
  label: string;
  icon: LucideIcon;
  // Shown only when the host exposes the feature (see Layout).
  feature?: "persistedOperations" | "stateMachines" | "bulkOperations";
}
interface NavGroup {
  label: string;
  icon: LucideIcon;
  items: NavItem[];
}

// The navigation api/0043 decides: what an operator works with, not the tables behind it. A group
// with one entry is drawn as a plain row.
const TOP: NavItem[] = [{ path: "/", label: "Overview", icon: LayoutDashboard }];

const GROUPS: NavGroup[] = [
  {
    label: "Runs",
    icon: Play,
    items: [
      { path: "/runs", label: "All runs", icon: Play },
      { path: "/runs/queued", label: "Queued", icon: Inbox },
      { path: "/runs/dead-letters", label: "Dead letters", icon: MailX },
    ],
  },
  { label: "Trains", icon: TrainFront, items: [{ path: "/trains", label: "Trains", icon: TrainFront }] },
  {
    label: "Machines",
    icon: Workflow,
    items: [{ path: "/machines", label: "Machines", icon: Workflow, feature: "stateMachines" }],
  },
  {
    label: "Schedules",
    icon: CalendarClock,
    items: [
      { path: "/schedules", label: "Manifests", icon: FileCog },
      { path: "/schedules/groups", label: "Manifest groups", icon: Layers },
    ],
  },
  {
    label: "System",
    icon: Settings,
    items: [
      { path: "/system/hosts", label: "Hosts", icon: Network },
      { path: "/system/scheduler", label: "Scheduler", icon: Server },
      { path: "/system/effects", label: "Effects", icon: Sparkles },
      { path: "/system/logs", label: "Logs", icon: ScrollText },
      { path: "/system/live", label: "Live events", icon: Radio },
      { path: "/system/operations", label: "Bulk operations", icon: ListChecks, feature: "bulkOperations" },
    ],
  },
  {
    label: "API admin",
    icon: Pin,
    items: [
      {
        path: "/api-admin/persisted-operations",
        label: "Persisted operations",
        icon: Pin,
        feature: "persistedOperations",
      },
    ],
  },
];

/** The entry for a path: the longest one it is, or sits under (`/runs/42` is All runs). */
function activeEntry(pathname: string, paths: string[]): string | undefined {
  return paths
    .filter((p) => (p === "/" ? pathname === "/" : pathname === p || pathname.startsWith(`${p}/`)))
    .sort((x, y) => y.length - x.length)[0];
}

// Sidebar icons: one size and stroke, inheriting the row's text colour.
const ICON = "size-4 shrink-0";

export function Layout() {
  const location = useLocation();
  const allPaths = [...TOP, ...GROUPS.flatMap((g) => g.items)].map((i) => i.path);
  const current = activeEntry(location.pathname, [...allPaths, "/preferences", "/dev/mock-overlay"]);
  const active = (path: string) => path === current;
  // Only surfaced when the app is running against the mock (globalThis.__mockStore is set).
  const mockActive = Boolean(getGlobalMockStore());
  // Persisted operations appear only on a host that serves their management to this caller
  // (UsePersistedOperations with GateManagement).
  const persistedOps = usePersistedOperationsAvailable().available;
  // Every host has machines; only the demo, before its recordings hold them, cannot show the page
  // (lib/answerable.ts).
  const stateMachines = useAnswers("Machines");
  // The demo lists bulk operations once its recordings hold them.
  const bulkOperations = useAnswers("BulkOperations");
  const shown = (i: NavItem) =>
    i.feature === "persistedOperations"
      ? persistedOps
      : i.feature === "stateMachines"
        ? stateMachines
        : i.feature === "bulkOperations"
          ? bulkOperations
          : true;
  const groups = GROUPS.map((g) => ({ ...g, items: g.items.filter(shown) })).filter((g) => g.items.length > 0);

  return (
    <div className="flex flex-col h-screen">
      {isDemo && <DemoBanner />}
      <div className="flex flex-1 min-h-0">
        <GlobalProgressBar />
        <aside className="w-60 bg-surface border-r border-line flex flex-col">
          <div className="p-6 border-b border-line">
            <h1 className="text-xl font-bold text-accent-fg">Trax</h1>
            <div className="flex items-center gap-2 mt-1">
              <p className="text-sm text-muted">Dashboard</p>
              {mockActive && (
                <span className="text-[10px] font-semibold px-1.5 py-0.5 rounded bg-warn-soft text-warn-fg">
                  MOCK
                </span>
              )}
            </div>
          </div>

          <nav className="flex-1 p-4 space-y-1 overflow-y-auto">
            {TOP.map((item) => (
              <NavLinkRow key={item.path} item={item} active={active(item.path)} />
            ))}
            {groups.map((group) =>
              group.items.length === 1 ? (
                <NavLinkRow key={group.label} item={group.items[0]} active={active(group.items[0].path)} />
              ) : (
                <NavGroupSection key={group.label} group={group} isActive={active} />
              ),
            )}
          </nav>

          <div className="p-4 border-t border-line space-y-2">
            {mockActive && (
              <Link
                to="/dev/mock-overlay"
                className={`flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium ${
                  active("/dev/mock-overlay")
                    ? "bg-warn-soft text-warn-fg"
                    : "text-warn-fg hover:bg-warn-soft"
                }`}
              >
                <FlaskConical className={ICON} />
                Mock overlay
              </Link>
            )}
            <NavLinkRow item={{ path: "/preferences", label: "Preferences", icon: User }} active={active("/preferences")} />
            <ConnectionIndicator />
            <ThemeToggle />
            {!isDemo && !hostAuthenticates() && (
              <button
                onClick={clearCredential}
                className="w-full text-left flex items-center gap-3 px-3 py-2 rounded-lg text-sm text-muted hover:bg-hover"
              >
                <KeyRound className={ICON} />
                Change credential
              </button>
            )}
          </div>
        </aside>

        <main className="flex-1 overflow-auto">
          <DashboardHeader />
          <div className="max-w-screen-2xl mx-auto p-8">
            <Outlet />
          </div>
          <DashboardFooter />
        </main>

        <ToastHost />
        <ConfirmHost />
        <NotificationBridge />
      </div>
    </div>
  );
}

function NavGroupSection({
  group,
  isActive,
}: {
  group: NavGroup;
  isActive: (path: string) => boolean;
}) {
  const [open, setOpen] = useState<boolean>(true);
  return (
    <div>
      <button
        onClick={() => setOpen((o) => !o)}
        className="w-full flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium text-fg-2 hover:bg-hover"
      >
        <group.icon className={ICON} />
        {group.label}
        {open ? (
          <ChevronDown className="ml-auto size-3.5 text-muted" />
        ) : (
          <ChevronRight className="ml-auto size-3.5 text-muted" />
        )}
      </button>
      {open && (
        <div className="ml-3 mt-1 space-y-1 border-l border-line pl-2">
          {group.items.map((item) => (
            <NavLinkRow key={item.path} item={item} active={isActive(item.path)} />
          ))}
        </div>
      )}
    </div>
  );
}

function NavLinkRow({ item, active }: { item: NavItem; active: boolean }) {
  return (
    <Link
      to={item.path}
      className={`flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium transition-colors ${
        active
          ? "bg-accent-soft text-accent-fg"
          : "text-fg-2 hover:bg-hover"
      }`}
    >
      <item.icon className={ICON} />
      {item.label}
    </Link>
  );
}
