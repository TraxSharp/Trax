import type { Metadata } from "next";

// The operations dashboard, served as a static build under /dashboard/demo/ (see
// scripts/build-dashboard-demo.sh) and framed here. It answers from recordings of real sample hosts,
// made by Trax.Samples' scripts/recordings/dashboard.mjs; dashboard/README.md says how.

export const metadata: Metadata = {
  title: "Dashboard",
  description:
    "The Trax operations dashboard, running in your browser on recordings of the Scheduling, Recovery and Persisted operations samples.",
};

const DEMO = "/dashboard/demo";

export default function DashboardPage() {
  return (
    // The dashboard fills the viewport below the header (h-16 plus its 1px border). Its own banner says
    // it runs on recordings. Below 1024px, the width it is laid out for, the frame scrolls sideways.
    <main className="h-[calc(100dvh-4rem-1px)] overflow-x-auto">
      <h1 className="sr-only">Dashboard</h1>
      <iframe
        src={DEMO}
        title="The Trax operations dashboard, running on recordings"
        className="block h-full w-full min-w-[1024px]"
      />
    </main>
  );
}
