// AUTO-GENERATED from booking.ir.json by generateContextTypes. Do not edit by hand.

export type BookingState = "Booked" | "CheckedIn" | "Issuing" | "Missed" | "NotIssued" | "Open" | "TurnedDown";
export type BookingTrigger = "CheckIn" | "GivePassport" | "Miss" | "OpenCheckIn";
export type BookingOutcome = "Issuing.cancelled" | "Issuing.done" | "Issuing.failed";

export type BookingBookedContext = {
  passport: string;
  seat?: string | null;
};

export type BookingCheckedInContext = {
  passport: string;
  seat?: string | null;
};

export type BookingIssuingContext = {
  passport: string;
  seat?: string | null;
};

export type BookingMissedContext = {
  passport: string;
  seat?: string | null;
};

export type BookingNotIssuedContext = {
  passport: string;
  seat?: string | null;
};

export type BookingOpenContext = {
  passport: string;
  seat?: string | null;
};

export type BookingTurnedDownContext = {
  passport: string;
  seat?: string | null;
};

export type BookingContexts = {
  Booked: BookingBookedContext;
  CheckedIn: BookingCheckedInContext;
  Issuing: BookingIssuingContext;
  Missed: BookingMissedContext;
  NotIssued: BookingNotIssuedContext;
  Open: BookingOpenContext;
  TurnedDown: BookingTurnedDownContext;
};

export type BookingCheckInInput = {
  seat: string;
};

export type BookingGivePassportInput = {
  passport: string;
};

export type BookingIssuingDoneOutput = {
  accepted: boolean;
};

export type BookingInputs = {
  CheckIn: BookingCheckInInput;
  GivePassport: BookingGivePassportInput;
  Miss: undefined;
  OpenCheckIn: undefined;
  "Issuing.cancelled": undefined;
  "Issuing.done": BookingIssuingDoneOutput;
  "Issuing.failed": undefined;
};

export type BookingSpec = {
  states: BookingContexts;
  triggers: BookingInputs;
};
