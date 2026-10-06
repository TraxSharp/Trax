// Captures real responses from the devhost for the dashboard's read operations and writes
// them to src/mock/fixtures/index.ts — the snapshot the fixture exchange replays offline.
//
//   npm run mock:capture              # needs the devhost running on :5310
//   FIXTURE_MAX_PAGES=3 npm run mock:capture
//
// Only queries are captured (mutations have side effects; subscriptions are driven by the
// simulator). List queries are walked page-by-page via nextCursor up to FIXTURE_MAX_PAGES, and
// a detail fixture is captured for the first few ids on each list's first page so clicking a
// row resolves. Host names / obvious PII are scrubbed before writing.
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { print, type DocumentNode } from "graphql";
import * as Q from "../src/graphql/queries";
import { EMPTY_VARIABLES_HASH, hashVariables } from "../src/mock/variables-hash";

const URL = process.env.TRAX_API_URL ?? "http://localhost:5310/trax/graphql";
const KEY = process.env.TRAX_API_KEY ?? "admin-key-do-not-use-in-production";
const MAX_PAGES = Number(process.env.FIXTURE_MAX_PAGES ?? 2);
const PAGE_SIZE = 25;

const here = dirname(fileURLToPath(import.meta.url));
const OUT_DIR = resolve(here, "../src/mock/fixtures");

type Data = Record<string, unknown>;
type Store = Record<string, Record<string, Data>>;
const store: Store = {};

function operationName(doc: DocumentNode): string {
  for (const def of doc.definitions) {
    if (def.kind === "OperationDefinition" && def.name) return def.name.value;
  }
  throw new Error("document has no named operation");
}

async function run(doc: DocumentNode, variables: Data): Promise<Data> {
  const res = await fetch(URL, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Api-Key": KEY },
    body: JSON.stringify({ query: print(doc), variables }),
  });
  const json = (await res.json()) as { data?: Data; errors?: unknown };
  if (json.errors) throw new Error(`${operationName(doc)}: ${JSON.stringify(json.errors)}`);
  return json.data!;
}

function getPath(obj: unknown, path: string): Data | undefined {
  return path.split(".").reduce<unknown>((acc, key) => {
    return acc && typeof acc === "object" ? (acc as Record<string, unknown>)[key] : undefined;
  }, obj) as Data | undefined;
}

const HOST_KEYS = new Set(["hostName", "hostInstanceId"]);
const EMAIL = /[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}/gi;
const PHONE = /\+?\d[\d ()-]{7,}\d/g;
// ISO timestamps look like phone numbers to PHONE ("2026-07-07" is ten digits and dashes), so
// they are left alone.
const ISO_DATE = /^\d{4}-\d{2}-\d{2}(T[\d:.]+(Z|[+-]\d{2}:\d{2})?)?$/;

function anonymize(value: unknown, key?: string): unknown {
  if (Array.isArray(value)) return value.map((v) => anonymize(v));
  if (value && typeof value === "object") {
    const out: Data = {};
    for (const [k, v] of Object.entries(value)) out[k] = anonymize(v, k);
    return out;
  }
  if (typeof value === "string") {
    if (key && HOST_KEYS.has(key)) return key === "hostName" ? "mock-host" : "mock-instance";
    if (ISO_DATE.test(value)) return value;
    return value.replace(EMAIL, "redacted@example.com").replace(PHONE, "555-0100");
  }
  return value;
}

function store_(op: string, hash: string, data: Data) {
  (store[op] ??= {})[hash] = anonymize(data) as Data;
}

// Walk a keyset-paginated list up to MAX_PAGES, keyed by the variables the dashboard sends.
async function capturePaged(doc: DocumentNode, base: Data, pagedPath: string): Promise<Data> {
  const op = operationName(doc);
  let afterId: number | undefined;
  let firstPage: Data = {};
  for (let page = 0; page < MAX_PAGES; page++) {
    const vars: Data = afterId == null ? { ...base } : { ...base, afterId };
    const data = await run(doc, vars);
    store_(op, hashVariables(vars), data);
    if (page === 0) firstPage = data;
    const paged = getPath(data, pagedPath);
    const next = paged?.nextCursor as number | null | undefined;
    if (next == null) break;
    afterId = next;
  }
  console.log(`  ${op}: ${Object.keys(store[op]).length} page(s)`);
  return firstPage;
}

async function captureOne(doc: DocumentNode, vars: Data): Promise<Data> {
  const data = await run(doc, vars);
  store_(operationName(doc), hashVariables(vars), data);
  return data;
}

async function main() {
  console.log(`Capturing fixtures from ${URL} (max ${MAX_PAGES} page(s) per list)`);

  // Non-paged reads.
  await captureOne(Q.OVERVIEW, { range: "LAST24_HOURS", hideAdmin: true });
  await captureOne(Q.TRAINS, { hideAdmin: true });
  await captureOne(Q.SCHEDULER_CONFIG, {});
  console.log("  Overview, Trains, SchedulerConfig");

  // Paged lists + a detail fixture for the first few ids on each first page.
  const lists: {
    doc: DocumentNode;
    pagedPath: string;
    itemsPath: string;
    detail?: DocumentNode;
  }[] = [
    { doc: Q.WORK_QUEUE, pagedPath: "operations.workQueue.workQueues", itemsPath: "operations.workQueue.workQueues.items", detail: Q.WORK_QUEUE_DETAIL },
    { doc: Q.DEAD_LETTERS, pagedPath: "operations.deadLetters.deadLetters", itemsPath: "operations.deadLetters.deadLetters.items", detail: Q.DEAD_LETTER_DETAIL },
    { doc: Q.LOGS, pagedPath: "operations.logs.logs", itemsPath: "operations.logs.logs.items" },
    { doc: Q.MANIFESTS, pagedPath: "operations.manifests", itemsPath: "operations.manifests.items", detail: Q.MANIFEST_DETAIL },
    { doc: Q.EXECUTIONS, pagedPath: "operations.executions", itemsPath: "operations.executions.items", detail: Q.EXECUTION_DETAIL },
    { doc: Q.MANIFEST_GROUPS, pagedPath: "operations.manifestGroups.groups", itemsPath: "operations.manifestGroups.groups.items", detail: Q.MANIFEST_GROUP_DETAIL },
  ];

  for (const { doc, pagedPath, itemsPath, detail } of lists) {
    const first = await capturePaged(doc, { take: PAGE_SIZE }, pagedPath);
    if (!detail) continue;
    const items = (getPath(first, itemsPath) as unknown as { id: number }[]) ?? [];
    for (const item of items.slice(0, 3)) await captureOne(detail, { id: item.id });
  }

  // Parent/child flow: metadata id 1 is seeded with children in the stress DB.
  await captureOne(Q.EXECUTION_DETAIL, { id: 1 });
  await capturePaged(Q.EXECUTION_CHILDREN, { parentId: 1, take: PAGE_SIZE }, "operations.executionChildren");

  mkdirSync(OUT_DIR, { recursive: true });
  const body =
    "// GENERATED by scripts/capture-fixtures.ts — do not edit by hand.\n" +
    "// Regenerate with `npm run mock:capture` while the devhost is running.\n" +
    "export type OperationFixtures = Record<string, Record<string, unknown>>;\n\n" +
    `export const EMPTY_VARIABLES_HASH = ${JSON.stringify(EMPTY_VARIABLES_HASH)};\n\n` +
    `export const operationFixtures: OperationFixtures = ${JSON.stringify(store, null, 2)};\n`;
  writeFileSync(resolve(OUT_DIR, "index.ts"), body);

  const ops = Object.keys(store).length;
  const entries = Object.values(store).reduce((n, m) => n + Object.keys(m).length, 0);
  console.log(`\nWrote ${ops} operations / ${entries} fixtures to src/mock/fixtures/index.ts`);
}

await main();
