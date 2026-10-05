// What each run's model call is about, as the host's CaseFiles holds it. The page shows it so the reader
// can see what the replay check hashes, and what "change the case" changes.

export interface OrderCase {
  orderId: string;
  amount: string;
  reason: string;
  priorRefunds: number;
}

export const ORDERS: OrderCase[] = [
  { orderId: "A-1001", amount: "$89.00", reason: "Arrived broken", priorRefunds: 0 },
  { orderId: "A-1002", amount: "$420.00", reason: "Never arrived", priorRefunds: 0 },
  { orderId: "A-1003", amount: "$35.50", reason: "Changed my mind", priorRefunds: 2 },
];

export const TOPICS = [
  "What the papers say about cold-weather battery wear",
  "History of the telegraph",
];

/** The audience every research run starts with, and the one "change the case" switches it to. */
export const AUDIENCE = { before: "engineers", after: "executives" };
