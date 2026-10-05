// Records the SignalRBroadcaster sample for traxsharp.net, which plays it back on its landing page. Start the
// sample as its README says (no database needed), then run
//
//   node signalr-broadcaster.mjs [--copy-to <dir>]
//
// It does what the sample's page does, over the same requests: asks the hub for a connection without signing in
// (refused), signs in as the demo operator, connects to the hub with the sign-in cookie, presses each ping button
// six times and signs out. Every request, every response and every TrainEvent the hub pushes is written down with
// the time it arrived, in out/signalr-broadcaster/recording.json. --copy-to also writes <dir>/signalr-recordings.json
// with the C# files the player shows, which is how the website's copy is refreshed
// (--copy-to ../../../Trax.Website/src/data). --copy-only skips recording.
//
// SIGNALR_HOST points the script at a host on another address than localhost:5270.

import { HubConnectionBuilder, HttpTransportType, LogLevel } from "@microsoft/signalr";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const HOST = process.env.SIGNALR_HOST ?? "localhost:5270";
const BASE = `http://${HOST}`;
const OUTCOMES = ["Succeed", "FailForClients", "FailUnexpectedly"];
const RUNS_PER_OUTCOME = 6;

// The C# the player shows, relative to samples/SignalRBroadcaster/Trax.Samples.SignalRBroadcaster.
const SOURCES = {
  program: "Program.cs",
  projection: "LiveTrainEvent.cs",
  junction: "Trains/Ping/Junctions/PingJunction.cs",
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function record() {
  const t0 = Date.now();
  const at = () => Date.now() - t0;
  const steps = {};

  // 1. Not signed in: the hub's negotiate request is refused before any connection opens.
  {
    const start = at();
    const res = await fetch(`${BASE}/hubs/trax-events/negotiate?negotiateVersion=1`, { method: "POST" });
    steps.probe = { start, status: res.status, statusText: res.statusText, end: at() };
    if (res.status !== 401) throw new Error(`negotiate without a cookie answered ${res.status}, not 401`);
  }

  // 2. Sign in as the demo operator, as the page's form does: a cookie and a redirect to /.
  let cookie;
  {
    const start = at();
    const res = await fetch(`${BASE}/demo/sign-in`, { method: "POST", redirect: "manual" });
    cookie = res.headers.getSetCookie().map((c) => c.split(";")[0]).join("; ");
    if (!cookie) throw new Error("sign-in set no cookie");
    const signedIn = at();
    const me = await fetch(`${BASE}/me`, { headers: { cookie } });
    const meBody = await me.json();
    steps.signIn = {
      start,
      status: res.status,
      location: res.headers.get("location"),
      setsCookie: true,
      signedIn,
      me: { status: me.status, body: meBody },
      end: at(),
    };
  }

  // 3. Connect to the hub with the cookie, and listen for the one message the page listens for.
  const events = [];
  const hub = new HubConnectionBuilder()
    .withUrl(`${BASE}/hubs/trax-events`, { headers: { cookie }, transport: HttpTransportType.WebSockets })
    .configureLogging(LogLevel.Warning)
    .build();
  hub.on("TrainEvent", (evt) => {
    events.push({ t: at(), evt });
  });
  {
    const start = at();
    await hub.start();
    steps.connect = { start, connectionId: hub.connectionId, end: at() };
  }

  // 4. Each button, a few times, one run at a time so each run's events are its own.
  const runs = [];
  for (let i = 0; i < RUNS_PER_OUTCOME; i++)
    for (const outcome of OUTCOMES) {
      const before = events.length;
      const start = at();
      const res = await fetch(`${BASE}/pings`, {
        method: "POST",
        headers: { "Content-Type": "application/json", cookie },
        body: JSON.stringify({ outcome }),
      });
      const answered = at();
      // The hub's events can trail the 202: wait for the run's last one.
      for (let w = 0; w < 100; w++) {
        const mine = events.slice(before);
        if (mine.some((e) => e.evt.eventType !== "Started")) break;
        await sleep(20);
      }
      await sleep(150);
      const mine = events.slice(before);
      if (mine.length < 2) throw new Error(`${outcome} run ${i}: ${mine.length} events`);
      runs.push({
        outcome,
        request: { start, body: { outcome } },
        response: { t: answered, status: res.status, statusText: res.statusText, body: await res.text() },
        events: mine,
      });
      await sleep(300);
    }

  // 5. Sign out: the form posts, the cookie is cleared and the page goes back to /.
  {
    const start = at();
    const res = await fetch(`${BASE}/sign-out`, { method: "POST", redirect: "manual", headers: { cookie } });
    await hub.stop();
    const me = await fetch(`${BASE}/me`, { headers: { cookie: "" } });
    steps.signOut = { start, status: res.status, location: res.headers.get("location"), me: me.status, end: at() };
  }

  return { recordedAt: new Date(t0).toISOString(), steps, runs };
}

/**
 * The website's copy: every time as milliseconds from the moment its own action started (the click), and each
 * event's server timestamp as milliseconds from the run's request, so the player can stamp them with the time it
 * plays them. External ids are kept as recorded.
 */
function compact(recording) {
  const shift = (s) => {
    const { start, end, signedIn, ...rest } = s;
    return { ...rest, ms: end - start, ...(signedIn != null ? { signedInMs: signedIn - start } : {}) };
  };
  const runs = recording.runs.map((run) => {
    const t0 = run.request.start;
    const serverT0 = Date.parse(run.events[0].evt.timestamp);
    return {
      outcome: run.outcome,
      body: run.request.body,
      response: { t: run.response.t - t0, status: run.response.status, statusText: run.response.statusText, body: run.response.body },
      events: run.events.map(({ t, evt }) => ({
        t: t - t0,
        serverMs: Date.parse(evt.timestamp) - serverT0,
        evt,
      })),
    };
  });
  return {
    probe: shift(recording.steps.probe),
    signIn: shift(recording.steps.signIn),
    connect: shift(recording.steps.connect),
    signOut: shift(recording.steps.signOut),
    runs,
  };
}

function writeWebsiteCopy(dir) {
  const recording = JSON.parse(readFileSync(join(here, "out", "signalr-broadcaster", "recording.json"), "utf8"));
  const project = resolve(here, "../../samples/SignalRBroadcaster/Trax.Samples.SignalRBroadcaster");
  const sources = Object.fromEntries(
    Object.entries(SOURCES).map(([key, file]) => [key, { file, code: readFileSync(join(project, file), "utf8").replace(/\r/g, "") }]),
  );
  mkdirSync(dir, { recursive: true });
  const file = join(dir, "signalr-recordings.json");
  writeFileSync(file, JSON.stringify({ sources, ...compact(recording) }) + "\n");
  console.log(`wrote ${file}`);
}

async function main() {
  const args = process.argv.slice(2);
  const copyAt = args.indexOf("--copy-to");
  const copyTo = copyAt >= 0 ? resolve(process.cwd(), args[copyAt + 1]) : null;
  if (args.includes("--copy-only")) {
    if (!copyTo) throw new Error("--copy-only needs --copy-to <dir>");
    return writeWebsiteCopy(copyTo);
  }

  const recording = await record();
  const out = join(here, "out", "signalr-broadcaster");
  mkdirSync(out, { recursive: true });
  writeFileSync(join(out, "recording.json"), JSON.stringify(recording, null, 2));
  console.log(`recorded ${recording.runs.length} runs, ${recording.runs.reduce((n, r) => n + r.events.length, 0)} hub messages`);
  if (copyTo) writeWebsiteCopy(copyTo);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
