// Smoke test: prove the exported SDL builds a mockable, executable schema and that a
// representative nested query resolves. Run: node scripts/mock-smoke.mjs
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { execute, parse } from "graphql";
import { addMocksToSchema } from "@graphql-tools/mock";
import { makeExecutableSchema } from "@graphql-tools/schema";

const here = dirname(fileURLToPath(import.meta.url));
const sdlSource = readFileSync(resolve(here, "../src/mock/schema-sdl.ts"), "utf8");
const match = sdlSource.match(/export const SCHEMA_SDL = (".*");\s*$/s);
if (!match) throw new Error("could not extract SCHEMA_SDL");
const SDL = JSON.parse(match[1]);

let longSeq = 1000;
let clockMs = 1_783_166_400_000;
const schema = addMocksToSchema({
  schema: makeExecutableSchema({ typeDefs: SDL }),
  mocks: {
    Long: () => longSeq++,
    DateTime: () => new Date((clockMs -= 60_000)).toISOString(),
    TimeSpan: () => "00:00:05",
    Any: () => null,
  },
  preserveResolvers: false,
});

const query = `{
  operations {
    workQueue {
      workQueues(take: 3) {
        totalCount
        items { id trainName status createdAt priority }
      }
    }
  }
}`;

const result = await execute({ schema, document: parse(query) });
if (result.errors) {
  console.error("EXECUTION ERRORS:", JSON.stringify(result.errors, null, 2));
  process.exit(1);
}
const wq = result.data.operations.workQueue.workQueues;
console.log(`OK: workQueues returned ${wq.items.length} items, totalCount=${wq.totalCount}`);
console.log("sample item:", JSON.stringify(wq.items[0]));
