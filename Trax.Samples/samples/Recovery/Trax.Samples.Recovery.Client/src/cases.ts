// What each run's model call is about, as the host's CaseFiles holds it.

import type { Scenario } from "./types";
import type { Column } from "./workflowView";

export const ORDERS = [
  { key: "A-1001", label: "A-1001: $89, arrived broken" },
  { key: "A-1002", label: "A-1002: $420, never arrived" },
  { key: "A-1003", label: "A-1003: $35.50, changed my mind, 2 earlier refunds" },
];

// Where a refund can crash: after its checkpoint (the retry starts there and only emails), before it (the retry
// replays both answers), or nowhere.
export const REFUND_CRASHES = [
  { key: "emailing", label: "Crash once while emailing" },
  { key: "paying", label: "Crash once while paying" },
  { key: "none", label: "Don't crash" },
] as const;

export type RefundCrashKey = (typeof REFUND_CRASHES)[number]["key"];

// Slices of the topic map's corpus. The model trusts shared references where most papers in the
// slice share one, so with the demo model the three slices take the gate's three tracks: Yes, Unsure, No.
export const SLICES = [
  { key: "all", label: "All three fields, 2016 to 2025", fromYear: 2016, toYear: 2025 },
  { key: "recent", label: "All three fields, 2021 to 2025", fromYear: 2021, toYear: 2025 },
  { key: "latest", label: "All three fields, 2022 to 2025", fromYear: 2022, toYear: 2025 },
];

/**
 * The timeline's boxes before the first run graph is read, so the grid is drawn in full from the start: what
 * columnsOf makes of each train's graph (workflowView.test.ts checks they agree). Once a run's graph arrives, the
 * page draws the boxes from it instead.
 */
export const COLUMNS: Record<Scenario, Column[]> = {
  REFUND: [
    { key: "LoadRefundCase#0", kind: "junction", label: "LoadRefundCase" },
    { key: "Decide<ChoiceDecision<Problem>>#0", kind: "question", label: "Problem?", questionKey: "Problem" },
    { key: "Switch<Problem>#0", kind: "route", label: "Problem track", questionKey: "Problem" },
    { key: "Decide<YesNoDecision<ApproveRefund>>#0", kind: "question", label: "ApproveRefund?", questionKey: "ApproveRefund" },
    { key: "Gate<ApproveRefund>#0", kind: "route", label: "ApproveRefund track", questionKey: "ApproveRefund" },
    { key: "Checkpoint<RefundOutcome>#0", kind: "checkpoint", label: "Checkpoint" },
    { key: "NotifyCustomer#0", kind: "junction", label: "NotifyCustomer" },
  ],
  TOPIC_MAP: [
    { key: "LoadCorpus#0", kind: "junction", label: "LoadCorpus" },
    { key: "Parallel#0/embedding/EmbeddingSimilarity#0", kind: "junction", label: "EmbeddingSimilarity", branch: "embedding" },
    { key: "Parallel#0/cocitation/CountSharedReferences#0", kind: "junction", label: "CountSharedReferences", branch: "cocitation" },
    {
      key: "Parallel#0/cocitation/Decide<YesNoDecision<SameTopic>>#0",
      kind: "question",
      label: "SameTopic?",
      questionKey: "SameTopic",
      branch: "cocitation",
    },
    { key: "Parallel#0/cocitation/Gate<SameTopic>#0", kind: "route", label: "SameTopic track", questionKey: "SameTopic", branch: "cocitation" },
    { key: "Parallel#0/authors/AuthorOverlap#0", kind: "junction", label: "AuthorOverlap", branch: "authors" },
    { key: "CombineSignals#0", kind: "junction", label: "CombineSignals" },
    { key: "FindHiddenTwins#0", kind: "junction", label: "FindHiddenTwins" },
  ],
};

/** The food order's scenarios, each armed the way scripts/recordings/order.mjs arms it. */
export const ORDER_SCENARIOS = [
  { key: "order", title: "Order a pizza", trouble: "NONE", end: "Delivered" },
  { key: "tablet-times-out", title: "The restaurant's tablet times out", trouble: "TABLET_TIMES_OUT", end: "Delivered" },
  { key: "restaurant-closed", title: "The restaurant is closed", trouble: "RESTAURANT_CLOSED", end: "Cancelled" },
  { key: "left-in-cart", title: "Left in the cart", trouble: "NONE", end: "Cleared" },
] as const;

export type OrderScenarioKey = (typeof ORDER_SCENARIOS)[number]["key"];
