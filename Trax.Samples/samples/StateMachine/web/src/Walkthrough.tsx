import type { MachineView } from "./machines";
import type { LastAction } from "./useMachine";

type Status = "ok" | "fail" | "skip";

interface Step {
  status: Status;
  text: string;
  detail?: string;
}

/**
 * The checks the server made for the page's last request, in the order it makes them, and where it stopped.
 * The order and the codes are the ones the state machine API documents (result codes): a refusal at one
 * step means nothing after it ran and nothing was written.
 */
export function Walkthrough({ machine, last, user }: { machine: MachineView; last: LastAction | null; user: string }) {
  if (!last)
    return (
      <div className="walk-empty">
        Fire a trigger. Each check the server makes, and where it stops, appears here.
      </div>
    );

  const steps = explain(machine, last, user);
  const refused = last.result.problem;
  return (
    <div className="walk">
      <div className={`walk-verdict ${refused ? "refused" : "accepted"}`}>
        <span className="walk-verdict-mark">{refused ? "✕" : "✓"}</span>
        <div>
          <div className="walk-verdict-title">{headline(last)}</div>
          <div className="walk-verdict-sub">
            {refused ? (
              <>
                Refused with <code>{refused.code}</code>. Nothing was stored.
              </>
            ) : (
              verdict(last)
            )}
          </div>
        </div>
      </div>
      <ol className="walk-steps">
        {steps.map((s, i) => (
          <li key={`${last.at}-${i}`} className={`walk-step walk-${s.status}`} style={{ animationDelay: `${i * 90}ms` }}>
            <span className="walk-dot">{s.status === "ok" ? "✓" : s.status === "fail" ? "✕" : ""}</span>
            <div>
              <div className="walk-text">{s.text}</div>
              {s.detail && <div className="walk-detail">{s.detail}</div>}
            </div>
          </li>
        ))}
      </ol>
    </div>
  );
}

function headline(last: LastAction): string {
  if (last.kind === "advance") {
    const coin = last.input?.coin;
    return `advanceSnapshot · ${last.trigger}${coin ? ` { coin: "${coin}" }` : ""}`;
  }
  if (last.kind === "send") return "sendSnapshot · the effect's move";
  return "saveSnapshot · a draft the page wrote";
}

function verdict(last: LastAction): string {
  const to = last.result.snapshot?.state;
  if (last.kind === "send" && last.replayed) return "The effect had already run: its receipt came back, and nothing was charged.";
  if (last.from && to && last.from !== to) return `The draft moved from ${last.from} to ${to}.`;
  return `The draft is in ${to ?? last.from}.`;
}

function explain(machine: MachineView, last: LastAction, user: string): Step[] {
  const code = last.result.problem?.code;
  const message = last.result.problem?.message;
  const from = last.from ?? "its current state";
  const stateRule = (name: string | undefined) => machine.states.find((s) => s.name === name)?.rule;
  const steps: Step[] = [];
  let stopped = false;

  // Adds a check: it fails when the response's code is one this check produces; after a failure, the rest is skipped.
  const check = (text: string, failsWith: string[], okDetail?: string, failDetail?: string) => {
    if (stopped) return steps.push({ status: "skip", text });
    if (code && failsWith.includes(code)) {
      stopped = true;
      return steps.push({ status: "fail", text, detail: failDetail ?? message });
    }
    return steps.push({ status: "ok", text, detail: okDetail });
  };
  const store = (text: string) => steps.push(stopped || code ? { status: "skip", text: "Nothing was stored." } : { status: "ok", text });

  if (last.kind === "advance") {
    const move = machine.moves.find((m) => m.trigger === last.trigger && m.from === last.from);
    check(`Loaded ${user}'s stored draft. It is in ${from}.`, ["not-found", "unauthenticated", "unknown-machine"]);
    check(
      `Is there a ${last.trigger} move out of ${from}?`,
      ["no-transition", "effect-bound"],
      move ? `Yes: ${move.from} → ${move.to}.` : undefined,
      code === "effect-bound"
        ? `Yes, but it runs the ${move?.effect} effect, so only sendSnapshot may fire it.`
        : `No. There is no ${last.trigger} arrow leaving ${from}, so the trigger means nothing here.`,
    );
    if (move?.guard) check(`Guard: ${move.guard}.`, ["guard-failed"], "Passed.");
    else steps.push({ status: stopped ? "skip" : "ok", text: "This move has no guard." });
    if (move) check(`${move.to} rule: ${stateRule(move.to)}.`, ["invalid-context"], "Holds.");
    store(`Stored the draft in ${move?.to ?? "its new state"}, for ${user} only.`);
  } else if (last.kind === "save") {
    const state = last.result.snapshot?.state ?? last.from ?? undefined;
    const total = typeof last.input?.total === "number" ? `$${((last.input.total as number) / 100).toFixed(2)}` : "?";
    const items = Array.isArray(last.input?.items) ? (last.input!.items as unknown[]).length : 0;
    steps.push({
      status: "ok",
      text: `Received a whole draft the page wrote: ${state}, ${items} item${items === 1 ? "" : "s"}, total ${total}.`,
      detail: "A save is the page's own copy, so the server checks it before keeping it.",
    });
    check(`Can a save put a draft in ${state}?`, ["state-reserved", "draft-committed", "draft-unreadable"], "Yes.");
    check(`${state} rule: ${stateRule(state)}.`, ["invalid-context", "malformed", "unknown-state"], "Holds.");
    store(`Stored it, for ${user} only.`);
  } else {
    const effectMove = machine.moves.find((m) => m.effect);
    steps.push({
      status: "ok",
      text: "The page sent only the draft's id.",
      detail: `No amount, no card, no call to a payment API: the ${effectMove?.effect} is the machine's to run.`,
    });
    check(`Loaded ${user}'s stored draft. It is in ${from}.`, ["not-found", "unauthenticated"]);
    if (last.replayed) {
      steps.push({
        status: "ok",
        text: `Has the ${effectMove?.effect} already run for this draft?`,
        detail: `Yes: it ran when the draft moved to ${effectMove?.to}. The server keeps a record of every effect it has run.`,
      });
      steps.push({ status: "ok", text: `Returned that run's receipt, without running the ${effectMove?.effect} again.` });
      steps.push({ status: "skip", text: "Nothing new to store." });
    } else {
      check(
        `Is the draft where the ${effectMove?.effect} runs from (${effectMove?.from})?`,
        ["no-transition", "no-effect"],
        "Yes.",
        `No. The ${effectMove?.effect} only runs on the ${effectMove?.trigger} move out of ${effectMove?.from}, so nothing ran.`,
      );
      check(
        `Ran the ${effectMove?.effect}, exactly once.`,
        ["delivery-failed", "effect-in-progress", "draft-changed", "request-id-reused"],
        receiptOf(last) ? `Receipt ${receiptOf(last)}.` : undefined,
      );
      check(`${effectMove?.to} rule: ${stateRule(effectMove?.to)}.`, ["invalid-context"], "Holds.");
      store(`Moved ${effectMove?.from} → ${effectMove?.to} and stored it, for ${user} only.`);
    }
  }

  // A refusal none of the checks above names still says what happened.
  if (code && !steps.some((s) => s.status === "fail")) steps.push({ status: "fail", text: `Refused: ${code}.`, detail: message });
  return steps;
}

function receiptOf(last: LastAction): string | undefined {
  const r = last.result.snapshot?.context.receipt;
  return typeof r === "string" ? r : undefined;
}
