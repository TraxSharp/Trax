// AUTO-GENERATED from turnstile.ir.json by generateContextTypes. Do not edit by hand.

export type TurnstileState = "Locked" | "Unlocked";
export type TurnstileTrigger = "Coin" | "Push";

export type TurnstileLockedContext = Record<string, never>;

export type TurnstileUnlockedContext = {
  paidWith: string;
};

export type TurnstileContexts = {
  Locked: TurnstileLockedContext;
  Unlocked: TurnstileUnlockedContext;
};

export type TurnstileCoinInput = {
  coin: string;
};

export type TurnstileInputs = {
  Coin: TurnstileCoinInput;
  Push: undefined;
};

export type TurnstileSpec = {
  states: TurnstileContexts;
  triggers: TurnstileInputs;
};
