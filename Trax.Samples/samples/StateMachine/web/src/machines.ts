// How the page describes the two machines: their states, the rule each state's context must satisfy, and the
// moves between them. It mirrors Machines.cs for display only; the server decides everything.

export interface StateView {
  name: string;
  /** What every draft in this state must satisfy, in words. */
  rule: string;
  /** Committed: only the machine's effect can put a draft here, and only its declared events move it out. */
  committed?: boolean;
}

export interface MoveView {
  trigger: string;
  from: string;
  to: string;
  /** The guard's rule, when the move has one. */
  guard?: string;
  /** The move runs this irreversible effect, so only a send fires it. */
  effect?: string;
}

export interface TriggerButton {
  trigger: string;
  input?: Record<string, unknown>;
  /** What the trigger means in the example, as a caption. */
  caption: string;
  /** The event bound to the effect: the server runs the effect when it is sent. */
  send?: boolean;
  /** A trigger the machine is expected to refuse, shown as a probe. */
  probe?: boolean;
}

export interface MachineView {
  name: "turnstile" | "checkout";
  title: string;
  tagline: string;
  /** The example, in one line: the machine is the point, this is only the costume. */
  example: string;
  states: StateView[];
  moves: MoveView[];
  triggers: TriggerButton[];
}

export const TURNSTILE: MachineView = {
  name: "turnstile",
  title: "A simple machine",
  tagline: "Two states, two triggers, one guard.",
  example: "Picture a turnstile: a coin unlocks it, and walking through locks it again.",
  states: [
    { name: "Locked", rule: "holds no context" },
    { name: "Unlocked", rule: "records which coin paid (paidWith)" },
  ],
  moves: [
    { trigger: "Coin", from: "Locked", to: "Unlocked", guard: "only a quarter or a dollar is accepted" },
    { trigger: "Push", from: "Unlocked", to: "Locked" },
  ],
  triggers: [
    { trigger: "Coin", input: { coin: "quarter" }, caption: "insert a quarter" },
    { trigger: "Coin", input: { coin: "dollar" }, caption: "insert a dollar" },
    { trigger: "Coin", input: { coin: "penny" }, caption: "insert a penny", probe: true },
    { trigger: "Push", caption: "walk through" },
  ],
};

export const CHECKOUT: MachineView = {
  name: "checkout",
  title: "A machine with an effect",
  tagline: "Three states, and one move that does something irreversible, exactly once.",
  example: "Picture a checkout: fill a cart, review it, pay. Paying charges a card, which must never happen twice.",
  states: [
    { name: "Cart", rule: "the total is $9.99 per item, and there is no receipt" },
    { name: "Review", rule: "at least one item, the total is $9.99 per item, and there is no receipt" },
    { name: "Paid", rule: "a receipt, and the total is $9.99 per item", committed: true },
  ],
  moves: [
    { trigger: "AddItem", from: "Cart", to: "Cart", guard: "names an item; the server prices the cart" },
    { trigger: "Next", from: "Cart", to: "Review", guard: "at least one item" },
    { trigger: "Back", from: "Review", to: "Cart" },
    { trigger: "Pay", from: "Review", to: "Paid", effect: "charge", guard: "needs items and a receipt" },
    { trigger: "Reset", from: "Paid", to: "Cart" },
  ],
  triggers: [
    { trigger: "Next", caption: "go to review" },
    { trigger: "Back", caption: "back to the cart" },
    { trigger: "Pay", caption: "pay: runs the charge", send: true },
    { trigger: "Reset", caption: "start over" },
  ],
};
