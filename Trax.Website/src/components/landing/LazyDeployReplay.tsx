"use client";

import dynamic from "next/dynamic";

// Every recorded deploy (DeployReplay) reads all of the deploy recordings, too big to load with the page. ShipIt puts
// this in a fold that draws it only once opened, so the recordings load then.
const LazyDeployReplay = dynamic(() => import("./DeployReplay"), {
  ssr: false,
  loading: () => <p className="text-sm text-text-muted">Loading the recordings…</p>,
});

export default LazyDeployReplay;
