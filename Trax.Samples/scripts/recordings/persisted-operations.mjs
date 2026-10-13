// Records the PersistedOperations sample for traxsharp.net, which plays it back on its landing page. Start the
// sample's API as its README says, on a fresh database (the client is not needed; this script does what it does),
// then run
//
//   node persisted-operations.mjs [--copy-to <dir>]
//
// The page tells three stories, and the script plays each against the sample's API with the exact requests the
// sample's client sends, writing down every request, its status, its response and how long it took:
//
//   calls    the shipped app calls greet_v1 by id; someone else sends a query of their own and is refused
//   fix      the operator changes greet_v1 to a formal greeting; the same call from the app now answers formally
//   refused  the operator tries an edit that renames the field the app reads; it is refused, and the app's answer stands
//
// Each story starts from the manifest as the app shipped with it (greet_v1 casual), uploaded first and not shown,
// and after every upload the script reads back what the store holds for greet_v1. The recording lands in
// out/persisted-operations/recording.json. --copy-to also writes it, with the sample's C# and its manifest, to
// <dir>/persisted-operations-recordings.json, which is how the website's copy is refreshed
// (--copy-to ../../../Trax.Website/src/data). --copy-only skips recording.

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const sample = resolve(here, "../../samples/PersistedOperations");
const HOST = process.env.PERSISTED_OPERATIONS_HOST ?? "localhost:5240";
const PATH = "/trax/graphql/";
const OPERATOR_KEY = "operator-key-do-not-use-in-production";

const manifest = JSON.parse(
  readFileSync(join(sample, "Trax.Samples.PersistedOperations.Client/manifest.json"), "utf8"),
).operations;
const SHIPPED = manifest.find((op) => op.id === "greet_v1").document;

// The client's fix and its refused edit, as Trax.Samples.PersistedOperations.Client sends them.
const FIX = SHIPPED.replace("style: CASUAL", "style: FORMAL");
const RENAME = FIX.replace("{ greeting }", "{ message: greeting }");
const INLINE = 'query { discover { greeting { greet(input: { name: "Eve", style: FORMAL }) { greeting } } } }';

const UPLOAD = `mutation Upload($input: UploadPersistedOperationInput!) {
  operations { persistedOperations { uploadPersistedOperation(input: $input) { success errors { code message } } } }
}`;

const READ = `query Stored($id: String!) {
  operations { persistedOperations { persistedOperation(id: $id) { document } } }
}`;

const SOURCES = [
  "Trax.Samples.PersistedOperations.Api/Program.cs",
  "Trax.Samples.PersistedOperations/Trains/Greeting/Greet/GreetTrain.cs",
  "Trax.Samples.PersistedOperations/Trains/Greeting/Greet/Junctions/ComposeGreetingJunction.cs",
];

/** Sends one request and returns it with the status, the response and how long it took. */
async function send(body, { operator = false } = {}) {
  const headers = { "Content-Type": "application/json" };
  if (operator) headers["X-Api-Key"] = OPERATOR_KEY;
  const started = performance.now();
  const response = await fetch(`http://${HOST}${PATH}`, { method: "POST", headers, body: JSON.stringify(body) });
  const text = await response.text();
  return {
    // The key is masked, as the page shows it.
    request: { headers: operator ? { "X-Api-Key": "operator key" } : {}, body },
    status: response.status,
    response: JSON.parse(text),
    ms: Math.round(performance.now() - started),
  };
}

const upload = (document) =>
  send({ query: UPLOAD, variables: { input: { id: "greet_v1", document, bypassShapeDiff: false } } }, { operator: true });

async function stored() {
  const { response } = await send({ query: READ, variables: { id: "greet_v1" } }, { operator: true });
  if (response.errors) throw new Error(`reading greet_v1: ${JSON.stringify(response.errors)}`);
  return response.data.operations.persistedOperations.persistedOperation?.document ?? null;
}

const accepted = (exchange) =>
  exchange.response.data?.operations?.persistedOperations?.uploadPersistedOperation?.success === true;

// Who sends each request, and what it is.
const steps = {
  appCalls: async () => ({ actor: "app", kind: "call", ...(await send({ id: "greet_v1", variables: { name: "Alice" } })) }),
  someoneElse: async () => ({ actor: "stranger", kind: "inline", ...(await send({ query: INLINE })) }),
  fix: async () => ({ actor: "operator", kind: "upload", document: FIX, ...(await upload(FIX)) }),
  rename: async () => ({ actor: "operator", kind: "upload", document: RENAME, ...(await upload(RENAME)) }),
};

const STORIES = [
  { key: "calls", title: "A shipped app calls the API", steps: ["appCalls", "someoneElse"] },
  { key: "fix", title: "Fix it without shipping a new app", steps: ["appCalls", "fix", "appCalls"] },
  { key: "refused", title: "A fix that would break the app is refused", steps: ["appCalls", "rename", "appCalls"] },
];

async function record() {
  if (await stored()) throw new Error("The store already holds greet_v1: start the API on a fresh database.");
  const stories = [];
  for (const story of STORIES) {
    // Every story starts where the app shipped: greet_v1 as the manifest has it.
    const reset = await upload(SHIPPED);
    if (!accepted(reset)) throw new Error(`could not restore greet_v1: ${JSON.stringify(reset.response)}`);
    const recorded = { key: story.key, title: story.title, storedBefore: await stored(), steps: [] };
    for (const name of story.steps) {
      const step = await steps[name]();
      if (step.kind === "upload") step.storedAfter = await stored();
      recorded.steps.push(step);
      console.log(`${story.key.padEnd(8)} ${name.padEnd(12)} ${step.status} ${step.ms} ms`);
    }
    stories.push(recorded);
  }
  return { recordedAt: new Date().toISOString(), host: HOST, stories };
}

function writeWebsiteCopy(dir, recording) {
  const sources = SOURCES.map((path) => ({ path, code: readFileSync(join(sample, path), "utf8").replace(/\r/g, "") }));
  mkdirSync(dir, { recursive: true });
  const file = join(dir, "persisted-operations-recordings.json");
  writeFileSync(file, JSON.stringify({ sources, manifest, stories: recording.stories }) + "\n");
  console.log(`wrote ${file}`);
}

async function main() {
  const args = process.argv.slice(2);
  const copyAt = args.indexOf("--copy-to");
  const copyTo = copyAt >= 0 ? resolve(process.cwd(), args[copyAt + 1]) : null;
  const out = join(here, "out", "persisted-operations");
  const file = join(out, "recording.json");

  let recording;
  if (args.includes("--copy-only")) {
    if (!copyTo) throw new Error("--copy-only needs --copy-to <dir>");
    recording = JSON.parse(readFileSync(file, "utf8"));
  } else {
    recording = await record();
    mkdirSync(out, { recursive: true });
    writeFileSync(file, JSON.stringify(recording, null, 2));
    console.log(`recorded ${file}`);
  }
  if (copyTo) writeWebsiteCopy(copyTo, recording);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
