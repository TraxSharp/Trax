// Writes the website's copy of the deploy recordings that deploy.sh made: one compact deploy-recordings.json, every
// time in milliseconds from the moment its scenario started.
//
//   node deploy.mjs --copy-to ../../../Trax.Website/src/data
//
// It reads out/deploy/<shape>/<target>/<scenario>/ and changes nothing that happened. What it leaves out, and why:
// - a pod's or container's events where nothing the page draws changed (kubectl reports every status update);
// - the migration's, drain's and resume's log lines but the ones each process writes about the drain and the
//   migration (their EF Core and hosting lines say nothing a reader needs);
// - the probe's own pod, and the throwaway pods the reach checks start, which are the recorder's, not the app's.
// Image names are cut to their tag, since the registry is the recording machine's own.
import { existsSync, readFileSync, readdirSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { simulated } from "./deploy-simulated.mjs";

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, "out", "deploy");
const args = process.argv.slice(2);
const copyTo = args.includes("--copy-to") ? args[args.indexOf("--copy-to") + 1] : null;
if (!copyTo) {
  console.error("usage: node deploy.mjs --copy-to <Trax.Website/src/data>");
  process.exit(1);
}

const SCENARIOS = ["first-deploy", "release-while-busy", "broken-release", "who-can-reach-what"];
const JOBS = new Set(["migrate", "drain", "resume"]);
const BACKING = new Set(["database", "rabbitmq"]);

const read = (file) => (existsSync(file) ? readFileSync(file, "utf8") : "");
const lines = (file) => read(file).split("\n").filter((l) => l.length > 0);
// RFC 3339 with nanoseconds, as kubectl and podman print it, to epoch milliseconds.
const iso = (s) => Date.parse(s.replace(/(\.\d{3})\d*/, "$1"));
const tagOf = (image) => (image ?? "").split(/[:@]/).slice(-1)[0];

function kindOf(service) {
  if (JOBS.has(service)) return "job";
  if (BACKING.has(service)) return "backing";
  return "app";
}

// One pod's life from kubectl's watch: each change to what the page draws. Pending is starting; a pod whose phase
// is Running is running until its readiness probe passes (a Job's pod has none), or crashing while a container waits
// to be restarted.
function podState(o, kind) {
  if (o.type === "DELETED") return "gone";
  if (o.deleting) return "stopping";
  if (o.phase === "Succeeded") return "done";
  if (o.phase === "Failed") return "failed";
  if (o.phase !== "Running") return "starting";
  if (kind === "job") return "running";
  if (o.ready) return "ready";
  return (o.containers ?? []).some((c) => c.state === "waiting") ? "crashing" : "running";
}

function kubernetesProcesses(dir, start) {
  const byName = new Map();
  for (const line of lines(join(dir, "pods.tsv"))) {
    const [ms, , json] = line.split("\t");
    const o = JSON.parse(json);
    if (!o.service || o.name === "hub-probe") continue;
    let p = byName.get(o.name);
    if (!p) {
      p = { name: o.name, service: o.service, kind: kindOf(o.service), tag: tagOf(o.image), events: [] };
      byName.set(o.name, p);
    }
    const state = podState(o, p.kind);
    const last = p.events[p.events.length - 1];
    if (last?.state === state || last?.state === "gone") continue;
    p.events.push({ t: Number(ms) - start, state });
  }
  // A Job's pod that had finished before the scenario started belongs to an earlier deploy.
  return [...byName.values()].filter((p) => !(p.kind === "job" && p.events[0]?.state === "done"));
}

// Each start of a container is a life of its own: a restarted unit runs a new container under the same name.
function podmanProcesses(dir, start, stack) {
  const lives = [];
  const open = new Map();
  for (const line of lines(join(dir, "containers.jsonl"))) {
    const e = JSON.parse(line);
    const m = e.name.slice(stack.length + 1).match(/^(.+)-pod-(.+)$/);
    if (!m || m[1] !== m[2]) continue;
    const service = m[1];
    const t = e.t - start;
    if (e.status === "start" || (e.status === "running" && !open.has(service))) {
      const life = { name: `${service}#${lives.filter((l) => l.service === service).length + 1}`, service, kind: kindOf(service), tag: tagOf(e.image), events: [{ t, state: "running" }] };
      lives.push(life);
      open.set(service, life);
    } else if (e.status === "died" && open.has(service)) {
      const life = open.get(service);
      // A job that exits 0 is done. A service that exits 0, or 143 (SIGTERM, as podman stops it), was stopped; any
      // other code is a failure, kept with the process so the page can say which.
      const stopped = e.exit === 0 || (life.kind !== "job" && e.exit === 143);
      life.events.push({ t, state: stopped ? (life.kind === "job" ? "done" : "gone") : "failed" });
      life.exit = e.exit ?? null;
      open.delete(service);
    }
  }
  return lives;
}

function probes(dir, start) {
  if (existsSync(join(dir, "probes.log")))
    return lines(join(dir, "probes.log")).map((l) => {
      const [ts, r] = l.split(" ");
      return [iso(ts) - start, r === "ok" ? 1 : 0];
    });
  return lines(join(dir, "probes.tsv")).map((l) => {
    const [ms, r] = l.split("\t");
    return [Number(ms) - start, r === "ok" ? 1 : 0];
  });
}

// The drain's and the migration's own words: the lines that say what they did or are waiting for.
const TELLS = /drain|busy|migrat|resum|still|InProgress/i;
function jobLines(dir, start) {
  const result = [];
  let pending = null;
  for (const line of lines(join(dir, "jobs.log"))) {
    const [job, rest] = [line.slice(0, line.indexOf("\t")), line.slice(line.indexOf("\t") + 1)];
    const ts = rest.slice(0, rest.indexOf(" "));
    const text = rest.slice(rest.indexOf(" ") + 1).trim();
    // .NET's console logger writes a category line, then the message indented on the next.
    if (/^(info|warn|fail|crit|dbug):/.test(text)) {
      pending = text;
      continue;
    }
    if (pending && TELLS.test(text) && !/EntityFramework|Hosting/.test(pending)) result.push({ t: iso(ts) - start, job, text });
    else if (!pending && TELLS.test(text)) result.push({ t: iso(ts) - start, job, text });
    pending = null;
  }
  return result.sort((a, b) => a.t - b.t);
}

function egress(dir, start) {
  return lines(join(dir, "egress-check.log"))
    .map((l) => {
      const ts = l.slice(0, l.indexOf(" "));
      const m = l.slice(l.indexOf(" ") + 1).match(/^(ok|FAIL)\s+(\S+)\s+(\S+)\s+(\S+)\s+\(want (\S+)\)/);
      return m ? { t: iso(ts) - start, ok: m[1] === "ok", kind: m[2], target: m[3], got: m[4], want: m[5] } : null;
    })
    .filter(Boolean);
}

function scenario(dir) {
  const meta = JSON.parse(read(join(dir, "meta.json")));
  const start = meta.start;
  const stack = `${JSON.parse(read(join(dir, "trax.deploy.json"))).app}-${meta.target === "kubernetes" ? "cluster" : "home"}`;
  const cli = lines(join(dir, "cli.tsv")).map((l) => {
    const [ms, from, ...text] = l.split("\t");
    return { t: Number(ms) - start, from, text: text.join("\t") };
  });
  const run = lines(join(dir, "run.tsv")).map((l) => {
    const [ms, state, drain] = l.split("\t");
    return { t: Number(ms) - start, state, drain: Number(drain) > 0 };
  });
  const checks = lines(join(dir, "checks.tsv")).map((l) => {
    const [ms, group, who, what, result, want] = l.split("\t");
    return { t: Number(ms) - start, group, who, what, result, want };
  });
  const processes = meta.target === "kubernetes" ? kubernetesProcesses(dir, start) : podmanProcesses(dir, start, stack);
  const probed = probes(dir, start);
  const jobs = jobLines(dir, start);
  const egressed = egress(dir, start);
  // The watchers are stopped just after the end is taken, so the last probe can land a moment after it: the
  // recording lasts until its last line.
  const last = Math.max(
    meta.end - start,
    ...cli.map((l) => l.t),
    ...probed.map(([t]) => t),
    ...processes.flatMap((p) => p.events.map((e) => e.t)),
    ...jobs.map((j) => j.t),
    ...egressed.map((e) => e.t),
    ...checks.map((c) => c.t),
    ...run.map((r) => r.t),
  );
  return {
    duration: last,
    probe: meta.probe,
    file: read(join(dir, "trax.deploy.json")),
    change: read(join(dir, "change.txt")).trim() || null,
    cli,
    processes,
    probes: probed,
    run: meta.run
      ? { id: meta.run.id, name: meta.run.name, busySeen: meta.run.busySeen - start, ended: meta.run.endedByRecorder - start, states: run }
      : null,
    jobs,
    checks,
    egress: egressed,
    before: meta.before ? tagOf(meta.before) : null,
    after: meta.after ? tagOf(meta.after) : null,
    exitCode: meta.exitCode ?? meta.installExit ?? null,
    recordedAt: new Date(start).toISOString(),
    commit: meta.commit,
  };
}

const shapes = {};
for (const shape of readdirSync(out).sort()) {
  const file = shape === "hub-workers" ? join(here, "..", "..", "samples", "DistributedWorkers", "trax.deploy.json") : join(here, "deploy", shape, "trax.deploy.json");
  const repoPath = relative(join(here, "..", "..", ".."), file);
  shapes[shape] = { file: { path: repoPath, code: read(file) }, targets: {} };
  for (const target of readdirSync(join(out, shape)).sort()) {
    const dir = join(out, shape, target);
    const scenarios = {};
    for (const key of SCENARIOS) if (existsSync(join(dir, key, "meta.json"))) scenarios[key] = scenario(join(dir, key));
    shapes[shape].targets[target] = { versions: lines(join(dir, "versions.txt")), scenarios };
  }
}

mkdirSync(copyTo, { recursive: true });
// AWS is not recorded; the page's simulated AWS picture says what it is built from (deploy-simulated.mjs).
const result = { recorder: "Trax.Samples/scripts/recordings/deploy.sh", shapes, simulated };
writeFileSync(join(copyTo, "deploy-recordings.json"), JSON.stringify(result) + "\n");
console.log(`wrote ${join(copyTo, "deploy-recordings.json")}`);
