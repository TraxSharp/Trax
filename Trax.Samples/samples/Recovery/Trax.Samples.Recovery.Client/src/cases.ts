// What each run's model call is about, as the host's CaseFiles holds it.

export const TOPICS = [
  { key: "papers", label: "What the papers say about cold-weather battery wear" },
  { key: "wiki", label: "History of the telegraph" },
];

export const ORDERS = [
  { key: "A-1001", label: "A-1001: $89, arrived broken" },
  { key: "A-1002", label: "A-1002: $420, never arrived" },
  { key: "A-1003", label: "A-1003: $35.50, changed my mind, 2 earlier refunds" },
];

// Slices of the topic map's corpus. The model trusts shared references where most papers in the
// slice share one, so with the demo model the three slices take the gate's three tracks: Yes, Unsure, No.
export const SLICES = [
  { key: "all", label: "All three fields, 2016 to 2025", fromYear: 2016, toYear: 2025 },
  { key: "recent", label: "All three fields, 2021 to 2025", fromYear: 2021, toYear: 2025 },
  { key: "latest", label: "All three fields, 2022 to 2025", fromYear: 2022, toYear: 2025 },
];
