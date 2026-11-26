// The player behind the home page's deploy replay (DeployReplay.tsx): recorded deploys in, what the page shows at a
// moment out. The recordings are src/data/deploy-recordings.json, made by Trax.Samples' scripts/recordings/deploy.sh
// on a throwaway machine and written here by deploy.mjs. Every time is milliseconds from the moment the scenario
// started. Nothing here makes a number up: counts, overlaps and gaps are read off the recording.

export type TargetKey = "kubernetes" | "podman";
export type ScenarioKey = "first-deploy" | "release-while-busy" | "broken-release" | "who-can-reach-what";
/**
 * starting: created, not running yet. running: its process runs (a Kubernetes pod not yet ready, a podman container,
 * a Job). ready: its readiness probe passes. crashing: a container waits to be restarted. stopping: being deleted, still
 * running. gone, done (a Job that succeeded), failed.
 */
export type ProcessState = "starting" | "running" | "ready" | "crashing" | "stopping" | "gone" | "done" | "failed";
export type ProcessKind = "app" | "job" | "backing";

export const SCENARIO_KEYS: ScenarioKey[] = ["first-deploy", "release-while-busy", "broken-release", "who-can-reach-what"];

export interface RecordedProcess {
  /** The pod's name, or for podman the service and which start of it this is ("hub#2"). */
  name: string;
  service: string;
  kind: ProcessKind;
  /** The image's tag: the time and commit it was built from. */
  tag: string;
  events: { t: number; state: ProcessState }[];
  exit?: number | null;
}

export interface CliLine {
  t: number;
  /** trax: the CLI printed it. you: a command the person runs, as the CLI told them to. shell: what that printed. */
  from: "trax" | "you" | "shell";
  text: string;
}

export interface Check {
  t: number;
  group: string;
  who: string;
  what: string;
  result: string;
  want: string;
}

export interface EgressCheck {
  t: number;
  ok: boolean;
  kind: string;
  target: string;
  got: string;
  want: string;
}

export interface DeployScenario {
  duration: number;
  /** The service the health probe hits. */
  probe: string;
  /** The trax.deploy.json this scenario deployed, as it was on disk. */
  file: string;
  /** What this run changed from the shape's own file, in a sentence, or null when it deployed it as it is. */
  change: string | null;
  cli: CliLine[];
  processes: RecordedProcess[];
  /** [t, 1 answered | 0 failed], one per probe. */
  probes: [number, number][];
  run: { id: number; name: string; busySeen: number; ended: number; states: { t: number; state: string; drain: boolean }[] } | null;
  jobs: { t: number; job: string; text: string }[];
  checks: Check[];
  egress: EgressCheck[];
  before: string | null;
  after: string | null;
  exitCode: number | null;
  recordedAt: string;
  commit: string;
}

export interface DeployTarget {
  versions: string[];
  scenarios: Partial<Record<ScenarioKey, DeployScenario>>;
}

export interface DeployShape {
  file: { path: string; code: string };
  targets: Partial<Record<TargetKey, DeployTarget>>;
}

export interface DeployRecordings {
  recorder: string;
  /** Recorded runs only: a shape's targets never include a simulated one. */
  shapes: Record<string, DeployShape>;
  /** Targets the page shows without a recording, and what each is built from. */
  simulated?: Record<string, { simulated: true; reason: string; modelledOn: { shape: string; target: string }; basis: { step: string; source: string }[] }>;
}

const JOB_ORDER = ["migrate", "drain", "resume"];
const BACKING_ORDER = ["database", "rabbitmq"];

/** The state a process is in at `t`, or null before it exists. */
export function stateAt(p: RecordedProcess, t: number): ProcessState | null {
  let state: ProcessState | null = null;
  for (const e of p.events) {
    if (e.t > t) break;
    state = e.state;
  }
  return state;
}

/** When a process first appears and when it ends (gone, done or failed for good), null while it still runs. */
export function spanOf(p: RecordedProcess): { from: number; to: number | null } {
  const from = p.events[0]?.t ?? 0;
  const end = [...p.events].reverse().find((e) => e.state === "gone" || e.state === "done" || (e.state === "failed" && p.kind !== "app"));
  return { from, to: end ? end.t : null };
}

export interface Bar {
  process: RecordedProcess;
  /** Which version it runs, named by the order the lane first saw each tag: "old", "new", or null for one version. */
  version: "old" | "new" | null;
  from: number;
  to: number | null;
}

export interface Lane {
  key: string;
  label: string;
  kind: ProcessKind;
  /** Bars packed into rows, so two at once sit one above the other. */
  rows: Bar[][];
}

/** The scenario's processes as lanes: the app's services (the probed one first), the jobs, then Postgres and RabbitMQ. */
export function lanesOf(s: DeployScenario): Lane[] {
  const services = [...new Set(s.processes.map((p) => p.service))];
  const rank = (service: string) => {
    const kind = s.processes.find((p) => p.service === service)?.kind;
    if (kind === "app") return service === s.probe ? 0 : 1;
    if (kind === "job") return 10 + JOB_ORDER.indexOf(service);
    return 20 + BACKING_ORDER.indexOf(service);
  };
  services.sort((a, b) => rank(a) - rank(b) || a.localeCompare(b));
  return services.map((service) => {
    const processes = s.processes.filter((p) => p.service === service).sort((a, b) => (a.events[0]?.t ?? 0) - (b.events[0]?.t ?? 0));
    const kind = processes[0]?.kind ?? "app";
    const tags = [...new Set(processes.map((p) => p.tag))];
    const rows: Bar[][] = [];
    for (const process of processes) {
      const { from, to } = spanOf(process);
      const version = kind === "app" && tags.length > 1 ? (process.tag === tags[0] ? "old" : "new") : null;
      const bar: Bar = { process, version, from, to };
      const row = rows.find((r) => {
        const last = r[r.length - 1];
        return last.to != null && last.to <= from;
      });
      if (row) row.push(bar);
      else rows.push([bar]);
    }
    const copies = new Set(processes.map((p) => p.name.replace(/#\d+$/, ""))).size;
    const label = kind === "app" && processes.length > 0 && tags.length === 1 && copies > 1 ? `${service} ×${copies}` : service;
    return { key: service, label, kind, rows };
  });
}

const ALIVE: ProcessState[] = ["running", "ready", "crashing", "stopping"];

/** The most copies of the probed service whose process ran at once, up to `t`: an old one stopping counts. */
export function mostRunningAt(s: DeployScenario, t: number): number {
  const mine = s.processes.filter((p) => p.service === s.probe);
  const times = [...new Set(mine.flatMap((p) => p.events.map((e) => e.t)))].filter((x) => x <= t).sort((a, b) => a - b);
  let most = 0;
  for (const at of times) most = Math.max(most, mine.filter((p) => ALIVE.includes(stateAt(p, at) as ProcessState)).length);
  return most;
}

export interface Health {
  answered: number;
  failed: number;
  /** Probes before the app first answered on a first deploy: it was not there yet, which is not a failure. */
  before: number;
  /** The longest stretch with no answer after the first one, in milliseconds, from its first failed probe to the next answer. */
  longestGap: number;
  firstAnswer: number | null;
}

/** The health probe's running count at `t`. */
export function healthAt(s: DeployScenario, t: number, firstDeploy = false): Health {
  let answered = 0;
  let failed = 0;
  let before = 0;
  let firstAnswer: number | null = null;
  let gapFrom: number | null = null;
  let longestGap = 0;
  for (const [at, ok] of s.probes) {
    if (at > t) break;
    if (ok) {
      answered++;
      if (firstAnswer == null) firstAnswer = at;
      if (gapFrom != null) longestGap = Math.max(longestGap, at - gapFrom);
      gapFrom = null;
    } else if (firstDeploy && firstAnswer == null) before++;
    else {
      failed++;
      if (gapFrom == null) gapFrom = at;
    }
  }
  if (gapFrom != null) longestGap = Math.max(longestGap, t - gapFrom);
  return { answered, failed, before, longestGap, firstAnswer };
}

/** The busy run's state, and whether a drain is on, at `t`. */
export function runAt(s: DeployScenario, t: number): { state: string; drain: boolean } | null {
  if (!s.run) return null;
  let at: { state: string; drain: boolean } | null = null;
  for (const r of s.run.states) {
    if (r.t > t) break;
    at = { state: r.state, drain: r.drain };
  }
  return at;
}

/** Where Back and Next stop: each line the CLI printed, each change to the run, each check. */
export function momentsOf(s: DeployScenario): number[] {
  return [
    ...s.cli.map((l) => l.t),
    ...(s.run?.states.map((r) => r.t) ?? []),
    ...s.checks.map((c) => c.t),
    ...(s.egress.length ? [s.egress[0].t] : []),
  ];
}

/** The line the CLI printed last before `t`: what the deploy is doing. */
export function captionAt(s: DeployScenario, t: number): string | null {
  let last: CliLine | null = null;
  for (const l of s.cli) if (l.t <= t && l.from !== "shell") last = l;
  return last ? (last.from === "you" ? `$ ${last.text}` : last.text.trim()) : null;
}

/**
 * The lines of the deployed file to light: the ones this scenario changed from the shape's own file, the environment
 * it deployed, and each service's project.
 */
export function litLinesOf(s: DeployScenario, original: string, target: TargetKey): number[] {
  const env = target === "kubernetes" ? "cluster" : "home";
  const before = new Set(original.split("\n").map((l) => l.trim()));
  const lit: number[] = [];
  s.file.split("\n").forEach((line, i) => {
    const trimmed = line.trim();
    if (!before.has(trimmed) || trimmed.startsWith(`"${env}": {`) || trimmed.includes('"project":')) lit.push(i + 1);
  });
  return lit;
}

/** m:ss of a time in milliseconds. */
export function clock(ms: number): string {
  const s = Math.max(0, Math.floor(ms / 1000));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
}

/** The domains the deployed file lets the probed service reach. */
export function egressOf(s: DeployScenario): string[] {
  try {
    const file = JSON.parse(s.file) as { services?: Record<string, { egress?: string[] }> };
    return file.services?.[s.probe]?.egress ?? [];
  } catch {
    return [];
  }
}

/** What the whole recording shows, in a sentence, with every number read off it: the section's claims. */
export function summaryOf(scenario: DeployScenario, scenarioKey: ScenarioKey, target: TargetKey): string {
  const end = scenario.duration;
  const h = healthAt(scenario, end, scenarioKey === "first-deploy");
  const probe = scenario.probe;
  let text: string;
  switch (scenarioKey) {
    case "first-deploy":
      text = `From nothing: Postgres, the migration, then the ${probe}. It first answered at ${clock(h.firstAnswer ?? end)}.`;
      break;
    case "release-while-busy":
      text =
        target === "kubernetes"
          ? `A run was still going, so the drain held the release until it ended. Then the new ${probe} started beside the old one: ${mostRunningAt(scenario, end)} ran at once, ${h.answered} probes answered and ${h.failed} failed.`
          : `A run was still going, so the drain held the release until it ended. Podman then restarts the ${probe} rather than running two: ${h.answered} probes answered, ${h.failed} failed while it restarted (${(h.longestGap / 1000).toFixed(1)}s without an answer).`;
      for (const p of scenario.processes.filter((x) => x.service === probe && x.exit != null && x.events.some((e) => e.state === "failed")))
        text += ` The old ${probe} exited with code ${p.exit} as it was stopped.`;
      break;
    case "broken-release":
      text =
        target === "kubernetes"
          ? `This release sets a value the ${probe} refuses at start. The new copy never got ready, so the deploy put the version before it back (exit code ${scenario.exitCode}): ${h.answered} probes answered, ${h.failed} failed.`
          : `This release sets a value the ${probe} refuses at start. Your machine has no rollback: the restarted ${probe} does not come back. ${h.answered} probes answered, ${h.failed} failed.`;
      break;
    case "who-can-reach-what": {
      const all = [...scenario.checks.map((c) => c.result === c.want), ...scenario.egress.map((e) => e.ok)];
      const listed = egressOf(scenario);
      const held = listed.length === 0 ? "may reach nothing outside the app" : `may reach ${listed.join(", ")} and nothing else outside the app`;
      // Both lists: a check from outside the app that got in, and an egress check from inside that got out.
      const wrong = [
        ...scenario.checks.filter((c) => c.result !== c.want).map((c) => `${c.who} to ${c.what}, ${c.result}`),
        ...scenario.egress.filter((e) => !e.ok).map((e) => `the ${probe} to ${e.target}, ${e.got}`),
      ];
      text =
        `${all.filter(Boolean).length} of ${all.length} checks came out as they should.` +
        (scenario.egress.length === 0
          ? " The egress was not checked: this run deployed without it."
          : scenario.egress.every((e) => e.ok)
            ? ` From inside, the ${probe} ${held}.`
            : "") +
        (wrong.length === 0 ? " Nothing outside the app reached it." : ` Not as it should: ${wrong.join("; ")}.`);
      break;
    }
  }
  return text;
}
