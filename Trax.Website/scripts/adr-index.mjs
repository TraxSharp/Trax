// Prints a JSON map of folder name to the ADR file paths in it, so the docs can link
// a citation like `Trax.Mediator/docs/adr/0004` to the exact file on GitHub.
//
// Usage: node scripts/adr-index.mjs <repository root>
//
// Every folder lives in the same repository as this site, so each corpus is read from
// disk: Trax.Docs/adr for the central decisions, <folder>/docs/adr for the rest.

import fs from "node:fs";
import path from "node:path";

const [repoRoot] = process.argv.slice(2);
const CODE_FOLDERS = [
  "Trax.Core",
  "Trax.Effect",
  "Trax.Mediator",
  "Trax.Scheduler",
  "Trax.Api",
  "Trax.Dashboard",
  "Trax.Cli",
  "Trax.Samples",
];
const ADR_FILE = /^\d{4}-.+\.md$/;

function listLocal(dir, prefix) {
  if (!fs.existsSync(dir)) return undefined;
  return fs
    .readdirSync(dir)
    .filter((name) => ADR_FILE.test(name))
    .sort()
    .map((name) => `${prefix}/${name}`);
}

const index = {};
const docs = listLocal(path.join(repoRoot, "Trax.Docs", "adr"), "adr");
if (docs) index["Trax.Docs"] = docs;

for (const folder of CODE_FOLDERS) {
  const files = listLocal(path.join(repoRoot, folder, "docs", "adr"), "docs/adr");
  if (files) index[folder] = files;
}

process.stdout.write(JSON.stringify(index, null, 2) + "\n");
