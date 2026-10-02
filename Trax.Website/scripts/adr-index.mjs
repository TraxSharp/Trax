// Prints a JSON map of repo name to the ADR file paths in it, so the docs can link
// a citation like `Trax.Mediator/docs/adr/0004` to the exact file on GitHub.
//
// Usage: node scripts/adr-index.mjs <Trax.Docs source dir> <workspace dir>
//
// Trax.Docs is read from the synced source. Every other repo is read from a sibling
// checkout in the workspace when there is one, else from the GitHub contents API.
// A repo that cannot be listed is left out, and its citations link to its ADR index.

import fs from "node:fs";
import path from "node:path";

const [docsDir, workspaceDir] = process.argv.slice(2);
const ORG = "TraxSharp";
const CODE_REPOS = [
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

async function listRemote(repo) {
  const headers = { Accept: "application/vnd.github+json" };
  if (process.env.GITHUB_TOKEN) {
    headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;
  }
  try {
    const response = await fetch(
      `https://api.github.com/repos/${ORG}/${repo}/contents/docs/adr?ref=main`,
      { headers, signal: AbortSignal.timeout(10_000) }
    );
    if (!response.ok) return undefined;
    const entries = await response.json();
    return entries
      .map((entry) => entry.name)
      .filter((name) => ADR_FILE.test(name))
      .sort()
      .map((name) => `docs/adr/${name}`);
  } catch {
    return undefined;
  }
}

const index = {};
const docs = listLocal(path.join(docsDir, "adr"), "adr");
if (docs) index["Trax.Docs"] = docs;

await Promise.all(
  CODE_REPOS.map(async (repo) => {
    const files =
      listLocal(path.join(workspaceDir, repo, "docs", "adr"), "docs/adr") ??
      (await listRemote(repo));
    if (files) index[repo] = files;
  })
);

process.stdout.write(JSON.stringify(index, null, 2) + "\n");
