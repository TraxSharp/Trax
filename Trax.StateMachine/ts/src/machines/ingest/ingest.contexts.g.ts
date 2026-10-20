// AUTO-GENERATED from ingest.ir.json by generateContextTypes. Do not edit by hand.

export type IngestState = "Approved" | "Cancelled" | "FetchFailed" | "Fetched" | "Fetching" | "Idle" | "NeedsReview";
export type IngestTrigger = "Abandon" | "Approve" | "Retry" | "Start";
export type IngestOutcome = "Fetching.cancelled" | "Fetching.done" | "Fetching.failed";

export type IngestApprovedContext = {
  fingerprint: string;
  source: string;
};

export type IngestCancelledContext = {
  source: string;
};

export type IngestFetchFailedContext = {
  source: string;
};

export type IngestFetchedContext = {
  fingerprint: string;
  source: string;
};

export type IngestFetchingContext = {
  source: string;
};

export type IngestIdleContext = {
  source: string;
};

export type IngestNeedsReviewContext = {
  fingerprint: string;
  source: string;
};

export type IngestContexts = {
  Approved: IngestApprovedContext;
  Cancelled: IngestCancelledContext;
  FetchFailed: IngestFetchFailedContext;
  Fetched: IngestFetchedContext;
  Fetching: IngestFetchingContext;
  Idle: IngestIdleContext;
  NeedsReview: IngestNeedsReviewContext;
};

export type IngestFetchingDoneOutput = {
  fingerprint: string;
  unsure: boolean;
};

export type IngestInputs = {
  Abandon: undefined;
  Approve: undefined;
  Retry: undefined;
  Start: undefined;
  "Fetching.cancelled": undefined;
  "Fetching.done": IngestFetchingDoneOutput;
  "Fetching.failed": undefined;
};

export type IngestSpec = {
  states: IngestContexts;
  triggers: IngestInputs;
};
