// A worker on the board: a client of the Trax host with a key of its own. Everything it sends goes through the client
// `trax client` generated from the host's manifest (src/trax): the claim and the board are its train functions, and
// the assignment is driven through the machine's twin and server. It never moves a ticket: it sends evidence, and the
// server's check decides.

import { ServerMachineController, type SendOutcome } from "@traxsharp/state-machine/client";
import { assignment, assignmentServer, claimWork, board, configureTrax, type ClaimWorkResult } from "./trax/index.g";

export const HOST = process.env.BOARD_HOST ?? "localhost:5290";
// This machine is reached over plain http, any other host over TLS: the client refuses to send a worker's key to another
// host in cleartext.
const LOCAL = /^(localhost|127(\.\d{1,3}){3}|\[::1\])(:\d+)?$/.test(HOST);
export const GRAPHQL_URL = `${LOCAL ? "http" : "https"}://${HOST}/trax/graphql`;
export const SOCKET_URL = `${LOCAL ? "ws" : "wss"}://${HOST}/trax/graphql`;

export type Lane = "triage" | "reproduce" | "fix" | "qa" | "review";

export interface WorkerSpec {
  name: string;
  /** The principal id the host knows this worker by: what a claim and an assignment name. */
  id: string;
  role: string;
  kind: "agent" | "person";
  lane: Lane;
  key: string;
}

/** One thing that happened, in the shape the website's recording reads (Trax.Samples/scripts/recordings/board.mjs). */
export interface BoardEvent {
  t: number;
  move: number;
  kind: "claim" | "package" | "start" | "send" | "answer" | "check" | "ticket";
  agent: string;
  ticketId: string;
  say?: string;
  /** What the move means, in a sentence: written by the script, on the move's first event. */
  caption?: string;
  /** Whether the move was a mistake or a finding, and the code that caught it: also on the move's first event. */
  mark?: Mark;
  claim?: { lane: string; problem: { code: string; message: string } | null };
  package?: { ticket: string; lane: string; instructions: string; tools: string[]; deadline: string };
  send?: { operation: string; input?: unknown };
  answer?: { state: string | null; revision: number | null; problem: { code: string; message: string } | null };
  check?: { train: string; status: "running" | "done" | "failed"; message: string };
  ticket?: { id: string; state: string; note: string | null; by: string | null };
  line?: string;
}

/** Where a worker's actions are written down, and the clock and move counter they share. */
export class Log {
  private readonly started = Date.now();
  private move = 0;
  readonly events: BoardEvent[] = [];
  /** The state each ticket was last seen in, so a move can name the transition it took. */
  readonly ticketState = new Map<string, string>();

  constructor(private readonly sink: (event: BoardEvent) => void = () => {}) {}

  next(): number {
    return ++this.move;
  }

  write(event: Omit<BoardEvent, "t">): void {
    const full = { t: Date.now() - this.started, ...event } as BoardEvent;
    this.events.push(full);
    this.sink(full);
  }
}

// The train each checking state invokes, for the recording.
const CHECKS: Record<string, string> = {
  CheckingTriage: "TriageTrain",
  CheckingRepro: "ReproduceTrain",
  CheckingClose: "CloseTrain",
  CheckingFix: "VerifyFixTrain",
  CheckingQa: "QaTrain",
  CheckingReview: "ReviewTrain",
};

// The ticket machine's trigger for each move between two of its states (TicketMachine.cs).
const TICKET_MOVES: Record<string, string> = {
  "Triage>Reproducing": "Triaged",
  "Reproducing>Reproducing": "NotReproduced",
  "Reproducing>ReadyToFix": "Reproduced",
  "Reproducing>Closed": "CannotReproduce",
  "ReadyToFix>ReadyToFix": "FixRejected",
  "ReadyToFix>InQA": "Fixed",
  "InQA>InReview": "QaPassed",
  "InQA>ReadyToFix": "QaFailed",
  "InReview>Done": "Approved",
  "InReview>ReadyToFix": "SentBack",
};

/** The events a worker sends: its lane's evidence. The outcomes are the server's own. */
export type Trigger = "Triage" | "SubmitRepro" | "Close" | "SubmitFix" | "SubmitChecks" | "Approve" | "SendBack";

/**
 * One move of a script: what the worker says, what the move means, and what the server is expected to answer. The
 * answer is the assignment's state once the check is done (Accepted, Rejected, Closed) or the code of a refusal, and
 * `ticket` the state the ticket is in afterwards. A host that answers anything else stops the run, so a caption
 * never describes something the server did not do.
 */
export interface Step {
  say?: string;
  caption?: string;
  mark?: Mark;
  expect: string;
  ticket?: string;
}

/**
 * A move worth pointing at. `not-allowed`: the worker tried a move the server refuses outright (a claim out of its
 * lane, an event its role may not send, a guard that does not hold). `bad-evidence`: the move was allowed, and the
 * server's check found the evidence wrong. `finding`: the check came back negative and that is the right answer (a
 * case that does not reproduce, QA finding a gap). `label` is a few words for the worker's badge, and `rule` names
 * the code that decided it, as a key of the recording's rules (scripts/recordings/board.mjs).
 */
export interface Mark {
  intent: "not-allowed" | "bad-evidence" | "finding";
  label: string;
  rule: string;
}

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

export class Worker {
  /** The assignment this worker holds, per ticket. */
  private readonly assignments = new Map<string, string>();

  constructor(
    readonly spec: WorkerSpec,
    private readonly log: Log,
  ) {}

  get name(): string {
    return this.spec.name;
  }

  // The generated client holds one connection for the whole process: each worker signs in before it acts.
  private signIn(): void {
    configureTrax({
      url: GRAPHQL_URL,
      socketUrl: SOCKET_URL,
      headers: () => ({ "X-Api-Key": this.spec.key }),
      connectionParams: () => ({ apiKey: this.spec.key }),
      user: this.spec.id,
    });
  }

  /** Claims the ticket in its lane and sends its evidence, as one move. */
  async take(ticketId: string, trigger: Trigger, input: unknown, step: Step): Promise<void> {
    const move = this.log.next();
    await this.claim(ticketId, { caption: step.caption, mark: step.mark, expect: "Claimed" }, move);
    await this.send(ticketId, trigger, input, { ...step, caption: undefined, mark: undefined }, move);
  }

  /** Starts an assignment for a ticket it never claimed and sends an event on it, as one move. */
  async sendUnclaimed(ticketId: string, trigger: Trigger, input: unknown, step: Step): Promise<void> {
    const move = this.log.next();
    await this.startAssignment(ticketId, move, step);
    await this.send(ticketId, trigger, input, { ...step, caption: undefined, mark: undefined }, move);
    await this.discard(ticketId);
  }

  /**
   * Asks for work in its lane, and starts an assignment on what it is given. `step.expect` is "Claimed", or the code
   * the server refuses the claim with.
   */
  async claim(ticketId: string, step: Step, move = this.log.next()): Promise<ClaimWorkResult> {
    this.signIn();
    const lane = this.spec.lane;
    const answer = await claimWork({ lane, ticketId });
    const output = answer?.output ?? null;
    this.log.write({
      move,
      kind: "claim",
      agent: this.name,
      ticketId,
      say: step.say,
      caption: step.caption,
      mark: step.mark,
      claim: { lane, problem: output?.problem ?? null },
      ...(output?.problem ? {} : { line: "ticket:event:Claim" }),
    });
    expected(this.name, ticketId, "claim", output?.problem?.code ?? (output?.ticket ? "Claimed" : null), step.expect);
    if (!output || output.problem || !output.ticket) return answer;

    this.log.write({
      move,
      kind: "package",
      agent: this.name,
      ticketId,
      package: {
        ticket: output.ticket.id,
        lane: output.lane,
        instructions: output.instructions,
        tools: output.tools,
        deadline: output.deadline,
      },
    });
    await this.startAssignment(ticketId, move);
    await this.ticket(ticketId, move, "ticket:event:Claim");
    return answer;
  }

  /** Starts an assignment for a ticket without claiming it first: the server lets it start and judges what it sends. */
  private async startAssignment(ticketId: string, move: number, told?: Pick<Step, "caption" | "mark">): Promise<void> {
    this.signIn();
    const controller = new ServerMachineController({
      machine: assignment,
      server: assignmentServer(),
      startInput: { ticketId },
    });
    try {
      await controller.start();
      const view = controller.get();
      if (view.id) this.assignments.set(ticketId, view.id);
      this.log.write({
        move,
        kind: "start",
        agent: this.name,
        ticketId,
        caption: told?.caption,
        mark: told?.mark,
        answer: {
          state: view.confirmed?.state ?? null,
          revision: view.revision,
          problem: view.problem ? { code: view.problem.code, message: view.problem.message } : null,
        },
        line: "assignment:start",
      });
    } finally {
      controller.dispose();
    }
  }

  /** Sends one event on its assignment for a ticket, and waits for the server's check when the event starts one. */
  async send(ticketId: string, trigger: Trigger, input: unknown, step: Step, move = this.log.next()): Promise<void> {
    const answered = await this.deliver(ticketId, trigger, input, step, move);
    expected(this.name, ticketId, trigger, answered, step.expect);
    if (step.ticket) expected(this.name, ticketId, `the ticket after ${trigger}`, this.log.ticketState.get(ticketId), step.ticket);
  }

  private async deliver(ticketId: string, trigger: Trigger, input: unknown, step: Step, move: number): Promise<string | null> {
    this.signIn();
    const id = this.assignments.get(ticketId);
    if (!id) throw new Error(`${this.name} holds no assignment for ${ticketId}.`);
    const controller = new ServerMachineController({ machine: assignment, server: assignmentServer(), id });
    try {
      await controller.start();
      this.log.write({
        move,
        kind: "send",
        agent: this.name,
        ticketId,
        say: step.say,
        caption: step.caption,
      mark: step.mark,
        send: { operation: lowerFirst(trigger), ...(input === undefined ? {} : { input }) },
        line: `assignment:event:${trigger}`,
      });
      const outcome: SendOutcome =
        input === undefined
          ? await (controller.send as (t: Trigger) => Promise<SendOutcome>)(trigger)
          : await (controller.send as (t: Trigger, i: unknown) => Promise<SendOutcome>)(trigger, input);

      if (outcome.outcome !== "confirmed") {
        const problem =
          outcome.outcome === "stopped"
            ? { code: outcome.error.code, message: outcome.error.message }
            : { code: outcome.problem.code, message: outcome.problem.message };
        this.log.write({
          move,
          kind: "answer",
          agent: this.name,
          ticketId,
          answer: { state: null, revision: null, problem },
          line: problem.code === "FORBIDDEN" ? `assignment:rule:${trigger}` : `assignment:guard:${trigger}`,
        });
        return problem.code;
      }

      const checking = outcome.snapshot.state;
      this.log.write({
        move,
        kind: "answer",
        agent: this.name,
        ticketId,
        answer: { state: checking, revision: outcome.snapshot.revision, problem: null },
        line: `assignment:state:${checking}`,
      });
      const train = CHECKS[checking];
      if (!train) return checking;
      this.log.write({
        move,
        kind: "check",
        agent: this.name,
        ticketId,
        check: { train, status: "running", message: "" },
        line: `assignment:state:${checking}`,
      });

      // The check's outcome is the server's: it arrives on the assignment's push.
      const deadline = Date.now() + 60_000;
      let view = controller.get();
      while (view.confirmed?.state === checking) {
        if (Date.now() > deadline) throw new Error(`${train} did not answer for ${ticketId} within a minute.`);
        await sleep(50);
        view = controller.get();
      }
      const ended = view.confirmed!;
      const context = ended.context as { verdict?: string | null };
      const accepted = ended.state !== "Rejected";
      this.log.write({
        move,
        kind: "check",
        agent: this.name,
        ticketId,
        check: { train, status: accepted ? "done" : "failed", message: context.verdict ?? "" },
        line: accepted ? "assignment:outcome:accepted" : "assignment:outcome:rejected",
      });
      this.log.write({
        move,
        kind: "answer",
        agent: this.name,
        ticketId,
        answer: { state: ended.state, revision: view.revision, problem: null },
        line: accepted ? "assignment:outcome:accepted" : "assignment:outcome:rejected",
      });
      await this.ticket(ticketId, move);
      return ended.state;
    } finally {
      controller.dispose();
    }
  }

  /** Lets go of an assignment it started but cannot use. */
  private async discard(ticketId: string): Promise<void> {
    this.signIn();
    const id = this.assignments.get(ticketId);
    if (!id) return;
    await assignmentServer().discard(id, crypto.randomUUID());
    this.assignments.delete(ticketId);
  }

  // Reads the ticket back from the board, as this worker sees it, after a move, and names the ticket machine's
  // transition it took: each move between two states is one trigger.
  private async ticket(ticketId: string, move: number, line?: string): Promise<void> {
    const answer = await board();
    const ticket = answer.tickets.find((t) => t.id === ticketId);
    if (!ticket) return;
    const before = this.log.ticketState.get(ticketId);
    this.log.ticketState.set(ticketId, ticket.state);
    line ??= before && TICKET_MOVES[`${before}>${ticket.state}`] ? `ticket:event:${TICKET_MOVES[`${before}>${ticket.state}`]}` : `ticket:state:${ticket.state}`;
    this.log.write({
      move,
      kind: "ticket",
      agent: this.name,
      ticketId,
      ticket: { id: ticket.id, state: ticket.state, note: ticket.note, by: ticket.by },
      line,
    });
  }
}

function expected(worker: string, ticketId: string, what: string, got: string | null | undefined, wanted: string): void {
  if (got !== wanted)
    throw new Error(`${worker} on ${ticketId}, ${what}: the server answered ${got ?? "nothing"}, the script expects ${wanted}.`);
}

function lowerFirst(value: string): string {
  return value.charAt(0).toLowerCase() + value.slice(1);
}
