// The moving picture at the front of the home page's deploy section (ReleaseStory.tsx): one release of the
// DistributedWorkers sample's hub on Kubernetes, told in about twenty seconds. Everything in it is read off the
// recorded run in src/data/deploy-recordings.json: the order things happened in, when each copy of the hub started,
// got ready and stopped, the job the release waited for, and every health request with its answer. Only the clock
// is compressed: long quiet stretches are shortened more than short busy ones, and nothing changes order.
//
// AWS has not been recorded (there is no account yet). Its picture is simulated: the same story, timed like the
// Kubernetes recording, with the steps the ECS deploy takes in the code (see `simulated` in the recordings). It has no
// requests and no counts, so nothing it shows can be mistaken for, or added to, a measured number.
import { lanesOf, type DeployRecordings, type DeployScenario, type RecordedProcess } from "./deploy-replay.ts";

export const STORY_MS = 24000;

/** About how many recorded requests are drawn per second of the picture: few enough to follow each one. */
export const DOTS_PER_SECOND = 1.2;

/** The shape and target the story is recorded from. */
export const STORY_SOURCE = { shape: "hub-workers", target: "kubernetes" } as const;

/** What the page says whenever the AWS picture is showing. */
export const SIMULATED_CAPTION = "Simulated from what the deploy does on ECS; not yet recorded on AWS.";

export type StoryKind = "normal" | "broken";
export type StoryTarget = "kubernetes" | "aws";

export interface SimulatedTarget {
  simulated: true;
  reason: string;
  modelledOn: { shape: string; target: string };
  /** What the ECS deploy does, with where in the code it does it. */
  basis: { step: string; source: string }[];
}

export interface StoryDot {
  /** Story time the request reached a version. */
  at: number;
  to: "v1" | "v2";
  ok: boolean;
}

/** One step of the deploy, lit from the moment the recording shows it start. */
export interface StoryStep {
  key: string;
  label: string;
  /** A short line under the step while it is the current one. */
  line: string;
  /** Story time it starts. */
  at: number;
  /** Recorded time it starts, from the CLI's line or the pod that shows it. */
  recorded: number;
}

export interface Story {
  kind: StoryKind;
  target: StoryTarget;
  simulated: boolean;
  /** The busy job: story times it ran from and to, or null when this release had none. */
  job: { from: number; to: number } | null;
  /** The release waiting on the job (the drain), in story time. */
  waiting: { from: number; to: number } | null;
  v1: { readyUntil: number; goneAt: number | null };
  v2: { from: number; readyAt: number | null; crashes: number[]; goneAt: number | null };
  /** A sample of the recorded requests, drawn as dots; empty when simulated. */
  dots: StoryDot[];
  /** One dot stands for this many recorded requests. */
  perDot: number;
  captions: { at: number; text: string }[];
  steps: StoryStep[];
  /** Maps story time back to recorded time, for the counter. */
  recordedAt: (storyMs: number) => number;
  /** The recorded scenario this story reads its counts from, or null when simulated. */
  scenario: DeployScenario | null;
}

/** A piecewise-linear clock from recorded time to story time through the moments given, kept in order. */
function compress(keys: number[], duration: number): { to: (t: number) => number; from: (s: number) => number } {
  const k = [...new Set([0, ...keys.filter((t) => t > 0 && t < duration), duration])].sort((a, b) => a - b);
  // Each stretch gets story time by the square root of its length: a long wait still reads as the longest, but
  // does not swallow the picture.
  const weights = k.slice(1).map((t, i) => Math.sqrt(t - k[i]));
  const total = weights.reduce((a, b) => a + b, 0) || 1;
  const s = [0];
  weights.forEach((w) => s.push(s[s.length - 1] + (w / total) * STORY_MS));
  const map = (xs: number[], ys: number[], x: number) => {
    if (x <= xs[0]) return ys[0];
    for (let i = 1; i < xs.length; i++)
      if (x <= xs[i]) return ys[i - 1] + ((x - xs[i - 1]) / Math.max(1, xs[i] - xs[i - 1])) * (ys[i] - ys[i - 1]);
    return ys[ys.length - 1];
  };
  return { to: (t) => map(k, s, t), from: (x) => map(s, k, x) };
}

const firstAt = (p: RecordedProcess | undefined, states: string[]) => p?.events.find((e) => states.includes(e.state))?.t ?? null;

/** The hub copies of a recorded run: the one serving when it began, and the new one. */
function hubsOf(s: DeployScenario) {
  const lane = lanesOf(s).find((l) => l.key === s.probe);
  const bars = lane?.rows.flat() ?? [];
  const old = bars.find((b) => b.version === "old")?.process ?? bars[0]?.process;
  const fresh = bars.find((b) => b.version === "new")?.process;
  return { old, fresh };
}

function recordedScenario(rec: DeployRecordings, kind: StoryKind): DeployScenario | null {
  const t = rec.shapes[STORY_SOURCE.shape]?.targets[STORY_SOURCE.target];
  return t?.scenarios[kind === "normal" ? "release-while-busy" : "broken-release"] ?? null;
}

/**
 * The story of one release. On Kubernetes it is the recording itself, compressed; on AWS (simulated) it takes the
 * same recording's timing and nothing else: no requests, no counts.
 */
export function storyOf(rec: DeployRecordings, kind: StoryKind, target: StoryTarget): Story | null {
  const s = recordedScenario(rec, kind);
  if (!s) return null;
  const simulated = target === "aws";
  if (simulated && rec.simulated?.aws?.simulated !== true) return null;
  const { old, fresh } = hubsOf(s);
  if (!old || !fresh) return null;

  const drainJob = s.processes.find((p) => p.service === "drain");
  const drainFrom = firstAt(drainJob, ["starting", "running"]);
  const drainTo = firstAt(drainJob, ["done", "failed"]);
  const jobFrom = s.run ? 0 : null;
  const jobTo = s.run ? s.run.ended : null;
  const v2From = fresh.events[0].t;
  const v2Ready = firstAt(fresh, ["ready"]);
  const v2Gone = firstAt(fresh, ["stopping", "gone"]);
  const crashes = fresh.events.filter((e) => e.state === "crashing").map((e) => e.t);
  const v1Stop = firstAt(old, ["stopping"]);
  const v1Gone = firstAt(old, ["gone"]);

  // The CLI's own lines mark the steps it takes; the hub's pods mark the ones the cluster takes.
  const line = (re: RegExp) => s.cli.find((l) => l.from === "trax" && re.test(l.text))?.t ?? null;
  const built = line(/^(Asking|Building|Pushing)/);
  const migrated = line(/^Migrating the database/);
  const drained = line(/^Draining:/);
  const rolledBack = line(/^Rolling the services back/);
  const resumed = line(/^Ending the drain/);

  const keys = [drainFrom, drainTo, jobTo, v2From, v2Ready, v1Stop, v1Gone, v2Gone, crashes[0], migrated, drained, rolledBack, resumed].filter(
    (t): t is number => t != null,
  );
  const clock = compress(keys, s.duration);
  const at = (t: number | null) => (t == null ? null : clock.to(t));

  // A sample of the recorded requests: about DOTS_PER_SECOND a second of story time, each going to a copy that was ready when
  // it was sent, taking turns when two were.
  const perDot = simulated ? 0 : Math.max(1, Math.round(s.probes.length / ((STORY_MS / 1000) * DOTS_PER_SECOND)));
  const dots: StoryDot[] = [];
  if (!simulated)
    s.probes.forEach(([t, ok], i) => {
      if (i % perDot !== 0) return;
      const v1Ready = t < (v1Stop ?? Infinity);
      const v2IsReady = v2Ready != null && t >= v2Ready && t < (v2Gone ?? Infinity);
      const to = v1Ready && v2IsReady ? (dots.length % 2 ? "v2" : "v1") : v2IsReady ? "v2" : "v1";
      dots.push({ at: clock.to(t), to, ok: ok === 1 });
    });

  const captions: { at: number; text: string }[] =
    kind === "normal"
      ? [
          { at: 0, text: "People are using the app, and a job is running on it." },
          ...(drainFrom != null ? [{ at: clock.to(drainFrom), text: "A new version is ready to go. It waits for the running job to finish first." }] : []),
          ...(jobTo != null ? [{ at: clock.to(jobTo), text: "The job is done, so the release goes ahead." }] : []),
          { at: clock.to(v2From), text: "The new version starts beside the old one. Requests still go to the old one." },
          ...(v2Ready != null ? [{ at: clock.to(v2Ready), text: "The new version is ready, so requests move over to it." }] : []),
          ...(v1Gone != null ? [{ at: clock.to(v1Gone), text: "The old version is gone. Nobody had to wait." }] : []),
        ]
      : [
          { at: 0, text: "People are using the app." },
          { at: clock.to(v2From), text: "A new version starts beside the old one, but it fails as it starts." },
          ...(crashes[0] != null ? [{ at: clock.to(crashes[0]), text: "It never gets ready, so requests stay on the old version." }] : []),
          ...(v2Gone != null ? [{ at: clock.to(v2Gone), text: "The deploy puts the old version back on its own. It never stopped answering." }] : []),
        ];

  const aws = target === "aws";
  const step = (key: string, label: string, line: string, t: number | null): StoryStep[] =>
    t == null ? [] : [{ key, label, line, at: clock.to(t), recorded: t }];
  const steps: StoryStep[] = [
    ...step("build", "Build", aws ? "Builds the images and pushes them to ECR." : "Builds the images and pushes them to the registry.", built ?? 0),
    ...step("migrate", "Migrate", aws ? "Runs the migration as an ECS task." : "Runs the migration as a Kubernetes Job.", migrated),
    ...step("drain", "Drain", aws ? "A drain task waits for the work still running." : "A drain Job waits for the work still running.", drained),
    ...step("start", "Start v2", aws ? "The rolling update starts a new task beside the old one." : "The new pod starts beside the old one.", v2From),
    ...(kind === "normal"
      ? [
          ...step("move", "Move requests", aws ? "The load balancer sends requests to the healthy new task." : "The Service sends requests to the ready new pod.", v2Ready),
          ...step("stop", "Stop v1", aws ? "The old task stops." : "The old pod stops.", v1Stop),
        ]
      : [
          ...step("never", "Never ready", aws ? "The new task never passes its health check." : "The new pod never passes its readiness check.", crashes[0] ?? null),
          ...step("back", "Put v1 back", aws ? "The circuit breaker rolls the service back." : "The deploy puts every service back as it was.", rolledBack ?? v2Gone),
        ]),
    ...step("resume", "Resume", aws ? "A task ends the drain." : "A Job ends the drain.", resumed),
  ];

  return {
    kind,
    target,
    simulated,
    steps,
    job: jobFrom != null && jobTo != null ? { from: clock.to(jobFrom), to: clock.to(jobTo) } : null,
    waiting: drainFrom != null && drainTo != null ? { from: clock.to(drainFrom), to: clock.to(drainTo) } : null,
    v1: { readyUntil: at(v1Stop) ?? STORY_MS, goneAt: at(v1Gone) },
    v2: { from: clock.to(v2From), readyAt: at(v2Ready), crashes: crashes.map((t) => clock.to(t)), goneAt: at(v2Gone) },
    dots,
    perDot,
    captions,
    recordedAt: clock.from,
    scenario: simulated ? null : s,
  };
}

/** The caption at a story time. */
export function captionAt(story: Story, at: number): string {
  let text = story.captions[0]?.text ?? "";
  for (const c of story.captions) if (c.at <= at) text = c.text;
  return text;
}

/**
 * The counter at a story time: every recorded request up to the matching recorded moment, answered and failed. Null
 * for a simulated story, which measured nothing.
 */
export function counterAt(story: Story, at: number): { answered: number; failed: number } | null {
  if (story.simulated || !story.scenario) return null;
  const until = story.recordedAt(at);
  let answered = 0;
  let failed = 0;
  for (const [t, ok] of story.scenario.probes) {
    if (t > until) break;
    if (ok) answered++;
    else failed++;
  }
  return { answered, failed };
}

/** What each box is called on each target. */
export const LABELS: Record<StoryTarget, { copy: string; router: string; job: string; wait: string }> = {
  kubernetes: { copy: "a pod", router: "the Service", job: "a running job", wait: "the drain Job waits" },
  aws: { copy: "an ECS task", router: "the load balancer", job: "a running job", wait: "the drain task waits" },
};

/** The step under way at a story time: the last one started, or -1 before the first. */
export function stepAt(story: Story, at: number): number {
  let current = -1;
  story.steps.forEach((step, i) => {
    if (step.at <= at) current = i;
  });
  return current;
}

/** What the picture is of, named for the target. */
export function sourceLabel(story: Story): string {
  return story.simulated ? "trax deploy on AWS (ECS), simulated" : "trax deploy on Kubernetes (k3s), recorded";
}
