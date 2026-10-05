// Records the PersistedOperations sample for traxsharp.net, which plays it back on its landing page. Start the
// sample's API as its README says, on a fresh database (the client is not needed; this script does what it does),
// then run
//
//   node persisted-operations.mjs [--copy-to <dir>]
//
// The script sends the sample's API the exact HTTP requests a visitor can choose on the page and writes down each
// request, its status and its response: an inline document, an inline document named dev_*, a call by id to
// greet_v1 and lookupUser_v1, an upload without a key, the manifest upload with the operator key, the hot-fix to
// greet_v1 and a shape-changing edit. It does so in each state the store can be in (nothing uploaded, the manifest
// uploaded, greet_v1 hot-fixed), so the page can take them in any order, and after every upload it reads back what
// the store holds. The recording lands in out/persisted-operations/recording.json. --copy-to also writes it, with
// the sample's C# and its manifest, to <dir>/persisted-operations-recordings.json, which is how the website's copy
// is refreshed (--copy-to ../../../Trax.Website/src/data). --copy-only skips recording.

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
const original = Object.fromEntries(manifest.map((op) => [op.id, op.document]));

// The client's hot-fix and its refused edit, as Trax.Samples.PersistedOperations.Client sends them.
const HOT_FIX =
  "query Greet($input: GreetInput!) { discover { greeting { greet(input: $input) { greetedAt greeting } } } }";
const SHAPE_CHANGE =
  "query Greet($input: GreetInput!) { discover { greeting { greet(input: $input) { greeting greetedAt __typename } } } }";

const UPLOAD = `mutation Upload($input: UploadPersistedOperationInput!) {
  operations {
    persistedOperations {
      uploadPersistedOperation(input: $input) {
        success
        errors { code message }
      }
    }
  }
}`;

const READ = `query Stored($id: String!) {
  operations { persistedOperations { persistedOperation(id: $id) {
    id operationName document shapeFingerprint isActive description } } }
}`;

const SOURCES = [
  "Trax.Samples.PersistedOperations.Api/Program.cs",
  "Trax.Samples.PersistedOperations/Trains/Greeting/Greet/GreetTrain.cs",
  "Trax.Samples.PersistedOperations/Trains/Greeting/Greet/Junctions/ComposeGreetingJunction.cs",
  "Trax.Samples.PersistedOperations/Trains/Users/LookupUser/LookupUserTrain.cs",
  "Trax.Samples.PersistedOperations/Trains/Users/LookupUser/Junctions/FetchUserJunction.cs",
];

/** Sends one request as the page shows it, and returns it with the status and the response. */
async function send(body, { operator = false } = {}) {
  const headers = { "Content-Type": "application/json" };
  if (operator) headers["X-Api-Key"] = OPERATOR_KEY;
  const started = performance.now();
  const response = await fetch(`http://${HOST}${PATH}`, { method: "POST", headers, body: JSON.stringify(body) });
  const text = await response.text();
  return {
    request: { method: "POST", path: PATH, headers, body },
    status: response.status,
    response: JSON.parse(text),
    durationMs: Math.round(performance.now() - started),
  };
}

const upload = (id, document, operator, description) =>
  send(
    { query: UPLOAD, variables: { input: { id, document, bypassShapeDiff: false, ...(description ? { description } : {}) } } },
    { operator },
  );

/** What the store holds for each id in the manifest, read with the operator key. */
async function readStore() {
  const store = {};
  for (const { id } of manifest) {
    const { response } = await send({ query: READ, variables: { id } }, { operator: true });
    if (response.errors) throw new Error(`reading ${id}: ${JSON.stringify(response.errors)}`);
    store[id] = response.data.operations.persistedOperations.persistedOperation;
  }
  return store;
}

const failed = (exchange) =>
  exchange.response.errors?.length > 0 ||
  exchange.response.data?.operations?.persistedOperations?.uploadPersistedOperation?.success === false;

/** Every action a visitor can take, keyed as the player keys it. Each sends one or more requests. */
const actions = {
  inline: () => [
    send({ query: '{ discover { greeting { greet(input: { name: "Eve" }) { greeting } } } }' }),
  ],
  devInline: () => [
    send({
      query: 'query dev_greet { discover { greeting { greet(input: { name: "Eve" }) { greeting } } } }',
      operationName: "dev_greet",
    }),
  ],
  "callGreet:Alice": () => [send({ id: "greet_v1", variables: { input: { name: "Alice" } } })],
  "callGreet:Eve": () => [send({ id: "greet_v1", variables: { input: { name: "Eve" } } })],
  "callLookup:user-42": () => [send({ id: "lookupUser_v1", variables: { input: { userId: "user-42" } } })],
  "callLookup:user-7": () => [send({ id: "lookupUser_v1", variables: { input: { userId: "user-7" } } })],
  uploadAnonymous: () => [upload("greet_v1", original.greet_v1, false)],
  upload: () => manifest.map((op) => upload(op.id, op.document, true)),
  hotfix: () => [upload("greet_v1", HOT_FIX, true, "demo hot-fix (fields reordered, same shape)")],
  shapeEdit: () => [upload("greet_v1", SHAPE_CHANGE, true)],
};

/** The actions each state offers, and the state an upload leaves the store in. */
const OFFERED = {
  empty: ["inline", "devInline", "callGreet:Alice", "callGreet:Eve", "callLookup:user-42", "callLookup:user-7", "uploadAnonymous", "upload"],
  uploaded: ["inline", "devInline", "callGreet:Alice", "callGreet:Eve", "callLookup:user-42", "callLookup:user-7", "uploadAnonymous", "shapeEdit", "upload", "hotfix"],
  hotfixed: ["inline", "devInline", "callGreet:Alice", "callGreet:Eve", "callLookup:user-42", "callLookup:user-7", "uploadAnonymous", "shapeEdit", "hotfix", "upload"],
};
const LEADS_TO = { upload: "uploaded", hotfix: "hotfixed" };

async function record() {
  const before = await readStore();
  if (Object.values(before).some(Boolean))
    throw new Error("The store already holds the manifest's ids: start the API on a fresh database.");

  const states = {};
  // Visit the states in the order the page reaches them; the action that moves to the next state is taken last.
  for (const [state, exit] of [["empty", "upload"], ["uploaded", "hotfix"], ["hotfixed", "upload"]]) {
    const store = await readStore();
    const recorded = {};
    for (const key of [...OFFERED[state].filter((k) => k !== exit), exit]) {
      const exchanges = [];
      for (const request of actions[key]()) exchanges.push(await request);
      const entry = { exchanges };
      if (LEADS_TO[key] && !exchanges.some(failed)) entry.storeAfter = await readStore();
      recorded[key] = entry;
      console.log(`${state.padEnd(8)} ${key.padEnd(20)} ${exchanges.map((e) => e.status).join(",")}`);
    }
    states[state] = { store, actions: recorded };
  }
  return { recordedAt: new Date().toISOString(), host: HOST, states, leadsTo: LEADS_TO };
}

function writeWebsiteCopy(dir, recording) {
  const sources = SOURCES.map((path) => ({ path, code: readFileSync(join(sample, path), "utf8").replace(/\r/g, "") }));
  mkdirSync(dir, { recursive: true });
  const file = join(dir, "persisted-operations-recordings.json");
  const { states, leadsTo } = recording;
  writeFileSync(file, JSON.stringify({ sources, manifest, states, leadsTo }) + "\n");
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
