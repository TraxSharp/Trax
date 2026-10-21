// AUTO-GENERATED from assignment.ir.json by generateContextTypes. Do not edit by hand.

export type AssignmentState = "Accepted" | "CheckingClose" | "CheckingFix" | "CheckingQa" | "CheckingRepro" | "CheckingReview" | "CheckingTriage" | "Closed" | "Rejected" | "Working";
export type AssignmentTrigger = "Approve" | "Close" | "SendBack" | "SubmitChecks" | "SubmitFix" | "SubmitRepro" | "Triage";
export type AssignmentOutcome = "CheckingClose.cancelled" | "CheckingClose.done" | "CheckingClose.failed" | "CheckingFix.cancelled" | "CheckingFix.done" | "CheckingFix.failed" | "CheckingQa.cancelled" | "CheckingQa.done" | "CheckingQa.failed" | "CheckingRepro.cancelled" | "CheckingRepro.done" | "CheckingRepro.failed" | "CheckingReview.cancelled" | "CheckingReview.done" | "CheckingReview.failed" | "CheckingTriage.cancelled" | "CheckingTriage.done" | "CheckingTriage.failed";

export type AssignmentAcceptedContext = {
  area?: string | null;
  decision?: string | null;
  expectedShipping?: number | null;
  failedCases?: number | null;
  freeFrom?: number | null;
  inclusive?: boolean | null;
  reason?: string | null;
  subtotal?: number | null;
  subtotals?: number[] | null;
  ticketId: string;
  verdict?: string | null;
};

export type AssignmentCheckingCloseContext = {
  area?: string | null;
  decision?: string | null;
  expectedShipping?: number | null;
  failedCases?: number | null;
  freeFrom?: number | null;
  inclusive?: boolean | null;
  reason?: string | null;
  subtotal?: number | null;
  subtotals?: number[] | null;
  ticketId: string;
  verdict?: string | null;
};

export type AssignmentCheckingFixContext = {
  area?: string | null;
  decision?: string | null;
  expectedShipping?: number | null;
  failedCases?: number | null;
  freeFrom?: number | null;
  inclusive?: boolean | null;
  reason?: string | null;
  subtotal?: number | null;
  subtotals?: number[] | null;
  ticketId: string;
  verdict?: string | null;
};

export type AssignmentCheckingQaContext = {
  area?: string | null;
  decision?: string | null;
  expectedShipping?: number | null;
  failedCases?: number | null;
  freeFrom?: number | null;
  inclusive?: boolean | null;
  reason?: string | null;
  subtotal?: number | null;
  subtotals?: number[] | null;
  ticketId: string;
  verdict?: string | null;
};

export type AssignmentCheckingReproContext = {
  area?: string | null;
  decision?: string | null;
  expectedShipping?: number | null;
  failedCases?: number | null;
  freeFrom?: number | null;
  inclusive?: boolean | null;
  reason?: string | null;
  subtotal?: number | null;
  subtotals?: number[] | null;
  ticketId: string;
  verdict?: string | null;
};

export type AssignmentCheckingReviewContext = {
  area?: string | null;
  decision?: string | null;
  expectedShipping?: number | null;
  failedCases?: number | null;
  freeFrom?: number | null;
  inclusive?: boolean | null;
  reason?: string | null;
  subtotal?: number | null;
  subtotals?: number[] | null;
  ticketId: string;
  verdict?: string | null;
};

export type AssignmentCheckingTriageContext = {
  area?: string | null;
  decision?: string | null;
  expectedShipping?: number | null;
  failedCases?: number | null;
  freeFrom?: number | null;
  inclusive?: boolean | null;
  reason?: string | null;
  subtotal?: number | null;
  subtotals?: number[] | null;
  ticketId: string;
  verdict?: string | null;
};

export type AssignmentClosedContext = {
  area?: string | null;
  decision?: string | null;
  expectedShipping?: number | null;
  failedCases?: number | null;
  freeFrom?: number | null;
  inclusive?: boolean | null;
  reason?: string | null;
  subtotal?: number | null;
  subtotals?: number[] | null;
  ticketId: string;
  verdict?: string | null;
};

export type AssignmentRejectedContext = {
  area?: string | null;
  decision?: string | null;
  expectedShipping?: number | null;
  failedCases?: number | null;
  freeFrom?: number | null;
  inclusive?: boolean | null;
  reason?: string | null;
  subtotal?: number | null;
  subtotals?: number[] | null;
  ticketId: string;
  verdict?: string | null;
};

export type AssignmentWorkingContext = {
  area?: string | null;
  decision?: string | null;
  expectedShipping?: number | null;
  failedCases?: number | null;
  freeFrom?: number | null;
  inclusive?: boolean | null;
  reason?: string | null;
  subtotal?: number | null;
  subtotals?: number[] | null;
  ticketId: string;
  verdict?: string | null;
};

export type AssignmentContexts = {
  Accepted: AssignmentAcceptedContext;
  CheckingClose: AssignmentCheckingCloseContext;
  CheckingFix: AssignmentCheckingFixContext;
  CheckingQa: AssignmentCheckingQaContext;
  CheckingRepro: AssignmentCheckingReproContext;
  CheckingReview: AssignmentCheckingReviewContext;
  CheckingTriage: AssignmentCheckingTriageContext;
  Closed: AssignmentClosedContext;
  Rejected: AssignmentRejectedContext;
  Working: AssignmentWorkingContext;
};

export type AssignmentSendBackInput = {
  reason: string;
};

export type AssignmentSubmitChecksInput = {
  subtotals: number[];
};

export type AssignmentSubmitFixInput = {
  freeFrom: number;
  inclusive: boolean;
};

export type AssignmentSubmitReproInput = {
  expectedShipping: number;
  subtotal: number;
};

export type AssignmentTriageInput = {
  area: string;
};

export type AssignmentCheckingCloseDoneOutput = {
  accepted: boolean;
  failedCases?: number | null;
  message: string;
};

export type AssignmentCheckingFixDoneOutput = {
  accepted: boolean;
  failedCases?: number | null;
  message: string;
};

export type AssignmentCheckingQaDoneOutput = {
  accepted: boolean;
  failedCases?: number | null;
  message: string;
};

export type AssignmentCheckingReproDoneOutput = {
  accepted: boolean;
  failedCases?: number | null;
  message: string;
};

export type AssignmentCheckingReviewDoneOutput = {
  accepted: boolean;
  failedCases?: number | null;
  message: string;
};

export type AssignmentCheckingTriageDoneOutput = {
  accepted: boolean;
  failedCases?: number | null;
  message: string;
};

export type AssignmentInputs = {
  Approve: undefined;
  Close: undefined;
  SendBack: AssignmentSendBackInput;
  SubmitChecks: AssignmentSubmitChecksInput;
  SubmitFix: AssignmentSubmitFixInput;
  SubmitRepro: AssignmentSubmitReproInput;
  Triage: AssignmentTriageInput;
  "CheckingClose.cancelled": undefined;
  "CheckingClose.done": AssignmentCheckingCloseDoneOutput;
  "CheckingClose.failed": undefined;
  "CheckingFix.cancelled": undefined;
  "CheckingFix.done": AssignmentCheckingFixDoneOutput;
  "CheckingFix.failed": undefined;
  "CheckingQa.cancelled": undefined;
  "CheckingQa.done": AssignmentCheckingQaDoneOutput;
  "CheckingQa.failed": undefined;
  "CheckingRepro.cancelled": undefined;
  "CheckingRepro.done": AssignmentCheckingReproDoneOutput;
  "CheckingRepro.failed": undefined;
  "CheckingReview.cancelled": undefined;
  "CheckingReview.done": AssignmentCheckingReviewDoneOutput;
  "CheckingReview.failed": undefined;
  "CheckingTriage.cancelled": undefined;
  "CheckingTriage.done": AssignmentCheckingTriageDoneOutput;
  "CheckingTriage.failed": undefined;
};

export type AssignmentSpec = {
  states: AssignmentContexts;
  triggers: AssignmentInputs;
};
