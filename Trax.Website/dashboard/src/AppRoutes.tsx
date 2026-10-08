import { Routes, Route } from "react-router-dom";
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

export function AppRoutes() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route path="/" element={<OverviewPage />} />
        <Route path="/executions" element={<ExecutionsPage />} />
        <Route path="/executions/:id" element={<ExecutionDetailPage />} />
        <Route path="/work-queue" element={<WorkQueuePage />} />
        <Route path="/work-queue/:id" element={<WorkQueueDetailPage />} />
        <Route path="/dead-letters" element={<DeadLettersPage />} />
        <Route path="/dead-letters/:id" element={<DeadLetterDetailPage />} />
        <Route path="/logs" element={<LogsPage />} />
        <Route path="/state-machines" element={<StateMachinesPage />} />
        <Route path="/state-machines/:machine/:owner/:id" element={<StateMachineInstancePage />} />
        <Route path="/manifests" element={<ManifestsPage />} />
        <Route path="/manifests/:id" element={<ManifestDetailPage />} />
        <Route path="/groups" element={<ManifestGroupsPage />} />
        <Route path="/groups/:id" element={<ManifestGroupDetailPage />} />
        <Route path="/persisted-operations" element={<PersistedOperationsPage />} />
        <Route path="/persisted-operations/:id" element={<PersistedOperationDetailPage />} />
        <Route path="/settings/user" element={<UserSettingsPage />} />
        <Route path="/settings/server" element={<SettingsPage />} />
        <Route path="/settings/effects" element={<EffectsSettingsPage />} />
        <Route path="/trains" element={<TrainsPage />} />
        <Route path="/trains/:name" element={<TrainDetailPage />} />
        <Route path="/cluster" element={<ClusterPage />} />
        <Route path="/realtime" element={<RealTimePage />} />
        <Route path="/dev/mock-overlay" element={<MockOverlayPage />} />
      </Route>
    </Routes>
  );
}
