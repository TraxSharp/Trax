import { useState } from "react";
import { Link, Outlet, useLocation } from "react-router-dom";
import {
  ChevronDown,
  ChevronRight,
  Database,
  FileCog,
  FlaskConical,
  Inbox,
  KeyRound,
  Layers,
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
import { clearCredential } from "../lib/auth";
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

interface NavItem {
  path: string;
  label: string;
  icon: LucideIcon;
  // Shown only when the host exposes the feature (see Layout).
  feature?: "persistedOperations" | "stateMachines";
}
interface NavGroup {
  label: string;
  icon: LucideIcon;
  items: NavItem[];
}

const TOP: NavItem[] = [
  { path: "/", label: "Overview", icon: LayoutDashboard },
  { path: "/trains", label: "Trains", icon: TrainFront },
  { path: "/cluster", label: "Cluster", icon: Network },
  { path: "/realtime", label: "Real-time", icon: Radio },
];

const GROUPS: NavGroup[] = [
  {
    label: "Data",
    icon: Database,
    items: [
      { path: "/executions", label: "Executions", icon: Play },
      { path: "/work-queue", label: "Work queue", icon: Inbox },
      { path: "/dead-letters", label: "Dead letters", icon: MailX },
      { path: "/manifests", label: "Manifests", icon: FileCog },
      { path: "/groups", label: "Manifest groups", icon: Layers },
      { path: "/logs", label: "Logs", icon: ScrollText },
      { path: "/state-machines", label: "State machines", icon: Workflow, feature: "stateMachines" },
      { path: "/persisted-operations", label: "Persisted ops", icon: Pin, feature: "persistedOperations" },
    ],
  },
  {
    label: "Settings",
    icon: Settings,
    items: [
      { path: "/settings/user", label: "User", icon: User },
      { path: "/settings/server", label: "Server", icon: Server },
      { path: "/settings/effects", label: "Effects", icon: Sparkles },
    ],
  },
];

// Sidebar icons: one size and stroke, inheriting the row's text colour.
const ICON = "size-4 shrink-0";

export function Layout() {
  const location = useLocation();
  const active = (path: string) =>
    path === "/"
      ? location.pathname === "/"
      : location.pathname.startsWith(path);
  // Only surfaced when the app is running against the mock (globalThis.__mockStore is set).
  const mockActive = Boolean(getGlobalMockStore());
  // Persisted operations appear only on a host that calls UsePersistedOperations, as in the
  // Blazor sidebar.
  const persistedOps = usePersistedOperationsAvailable().available;
  // Every host has state machines in the Blazor sidebar; only the demo, before its recordings hold
  // them, cannot show the page (lib/answerable.ts).
  const stateMachines = useAnswers("MachineInstances");
  const shown = (i: NavItem) =>
    i.feature === "persistedOperations" ? persistedOps : i.feature === "stateMachines" ? stateMachines : true;
  const groups = GROUPS.map((g) => ({ ...g, items: g.items.filter(shown) }));

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
            {groups.map((group) => (
              <NavGroupSection
                key={group.label}
                group={group}
                isActive={active}
              />
            ))}
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
            <ConnectionIndicator />
            <ThemeToggle />
            {!isDemo && (
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
          <div className="max-w-6xl mx-auto p-8">
            <Outlet />
          </div>
          <DashboardFooter />
        </main>

        <ToastHost />
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
