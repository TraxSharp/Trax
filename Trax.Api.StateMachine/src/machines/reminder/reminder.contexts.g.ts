// AUTO-GENERATED from reminder.ir.json by generateContextTypes. Do not edit by hand.

export type ReminderState = "Done" | "Expired" | "Idle" | "Review";
export type ReminderTrigger = "Approve" | "Extend" | "Open";

export type ReminderDoneContext = Record<string, never>;

export type ReminderExpiredContext = Record<string, never>;

export type ReminderIdleContext = Record<string, never>;

export type ReminderReviewContext = Record<string, never>;

export type ReminderContexts = {
  Done: ReminderDoneContext;
  Expired: ReminderExpiredContext;
  Idle: ReminderIdleContext;
  Review: ReminderReviewContext;
};

export type ReminderInputs = {
  Approve: undefined;
  Extend: undefined;
  Open: undefined;
};

export type ReminderSpec = {
  states: ReminderContexts;
  triggers: ReminderInputs;
};
