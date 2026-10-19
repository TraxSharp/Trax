import { Navigate, Routes, Route, useLocation, useParams } from "react-router-dom";
import { Layout } from "./components/Layout";
import { OverviewPage } from "./pages/OverviewPage";
import { TrainsPage } from "./pages/TrainsPage";
import { TrainDetailPage } from "./pages/TrainDetailPage";
import { ClusterPage } from "./pages/ClusterPage";
import { RealTimePage } from "./pages/RealTimePage";
import { ExecutionsPage } from "./pages/ExecutionsPage";
import { WorkQueuePage } from "./pages/WorkQueuePage";
import { DeadLettersPage } from "./pages/DeadLettersPage";
import { LogsPage } from "./pages/LogsPage";
import { StateMachinesPage } from "./pages/StateMachinesPage";
import { MachinePage } from "./pages/MachinePage";
import { StateMachineInstancePage } from "./pages/StateMachineInstancePage";
import { ManifestsPage } from "./pages/ManifestsPage";
import { ManifestGroupsPage } from "./pages/ManifestGroupsPage";
import { ManifestGroupDetailPage } from "./pages/ManifestGroupDetailPage";
import { SettingsPage } from "./pages/SettingsPage";
import { UserSettingsPage } from "./pages/UserSettingsPage";
import { EffectsSettingsPage } from "./pages/EffectsSettingsPage";
import { PersistedOperationsPage } from "./pages/PersistedOperationsPage";
import { PersistedOperationDetailPage } from "./pages/PersistedOperationDetailPage";
import { ExecutionDetailPage } from "./pages/ExecutionDetailPage";
import { WorkQueueDetailPage } from "./pages/WorkQueueDetailPage";
import { DeadLetterDetailPage } from "./pages/DeadLetterDetailPage";
import { ManifestDetailPage } from "./pages/ManifestDetailPage";
import { MockOverlayPage } from "./pages/dev/MockOverlayPage";
import { BulkOperationPage, BulkOperationsPage } from "./pages/BulkOperationsPage";

// The console's routes follow its navigation (api/0043): Runs (with its Queued and Dead letter views),
// Trains, Machines, Schedules, System, API admin. The routes before it moved keep working as
// redirects, so a link already pasted into a log line or a ticket still lands.
export function AppRoutes() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route path="/" element={<OverviewPage />} />
        <Route path="/runs" element={<ExecutionsPage />} />
        <Route path="/runs/queued" element={<WorkQueuePage />} />
        <Route path="/runs/queued/:id" element={<WorkQueueDetailPage />} />
        <Route path="/runs/dead-letters" element={<DeadLettersPage />} />
        <Route path="/runs/dead-letters/:id" element={<DeadLetterDetailPage />} />
        <Route path="/runs/:id" element={<ExecutionDetailPage />} />
        <Route path="/trains" element={<TrainsPage />} />
        <Route path="/trains/:name" element={<TrainDetailPage />} />
        <Route path="/machines" element={<StateMachinesPage />} />
        <Route path="/machines/:name" element={<MachinePage />} />
        <Route path="/machines/:machine/:owner/:id" element={<StateMachineInstancePage />} />
        <Route path="/schedules" element={<ManifestsPage />} />
        <Route path="/schedules/groups" element={<ManifestGroupsPage />} />
        <Route path="/schedules/groups/:id" element={<ManifestGroupDetailPage />} />
        <Route path="/schedules/:id" element={<ManifestDetailPage />} />
        <Route path="/system/hosts" element={<ClusterPage />} />
        <Route path="/system/scheduler" element={<SettingsPage />} />
        <Route path="/system/effects" element={<EffectsSettingsPage />} />
        <Route path="/system/logs" element={<LogsPage />} />
        <Route path="/system/live" element={<RealTimePage />} />
        <Route path="/system/operations" element={<BulkOperationsPage />} />
        <Route path="/system/operations/:id" element={<BulkOperationPage />} />
        <Route path="/api-admin/persisted-operations" element={<PersistedOperationsPage />} />
        <Route path="/api-admin/persisted-operations/:id" element={<PersistedOperationDetailPage />} />
        <Route path="/preferences" element={<UserSettingsPage />} />
        <Route path="/dev/mock-overlay" element={<MockOverlayPage />} />

        {MOVED.map(([from, to]) => (
          <Route key={from} path={from} element={<Moved to={to} />} />
        ))}
      </Route>
    </Routes>
  );
}

/** Each route the console had before api/0043, and where it lives now (`:params` carried over). */
const MOVED: [string, string][] = [
  ["/executions", "/runs"],
  ["/executions/:id", "/runs/:id"],
  ["/work-queue", "/runs/queued"],
  ["/work-queue/:id", "/runs/queued/:id"],
  ["/dead-letters", "/runs/dead-letters"],
  ["/dead-letters/:id", "/runs/dead-letters/:id"],
  ["/state-machines", "/machines"],
  ["/state-machines/:machine/:owner/:id", "/machines/:machine/:owner/:id"],
  ["/manifests", "/schedules"],
  ["/manifests/:id", "/schedules/:id"],
  ["/groups", "/schedules/groups"],
  ["/groups/:id", "/schedules/groups/:id"],
  ["/cluster", "/system/hosts"],
  ["/realtime", "/system/live"],
  ["/logs", "/system/logs"],
  ["/settings/server", "/system/scheduler"],
  ["/settings/effects", "/system/effects"],
  ["/settings/user", "/preferences"],
  ["/persisted-operations", "/api-admin/persisted-operations"],
  ["/persisted-operations/:id", "/api-admin/persisted-operations/:id"],
];

function Moved({ to }: { to: string }) {
  const params = useParams();
  const { search, hash } = useLocation();
  const path = to.replace(/:(\w+)/g, (_, name: string) => encodeURIComponent(params[name] ?? ""));
  return <Navigate to={`${path}${search}${hash}`} replace />;
}
