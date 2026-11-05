import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

// Run after `npm run build`. Every file package.json names (main, module, types and each target in "exports") must
// be built and in the tarball npm would publish. Neither tsup nor `npm pack` fails on a missing one: a build that
// stops writing index.d.cts publishes a package whose CommonJS consumers get no types.

const root = fileURLToPath(new URL("..", import.meta.url));
const manifest = JSON.parse(readFileSync(join(root, "package.json"), "utf8"));

/** Every relative file path under an exports value, whatever its nesting of conditions. */
function targets(value) {
  if (typeof value === "string") return [value];
  if (value === null || typeof value !== "object") return [];
  return Object.values(value).flatMap(targets);
}

const named = [manifest.main, manifest.module, manifest.types, ...targets(manifest.exports)]
  .filter((p) => typeof p === "string")
  .map((p) => p.replace(/^\.\//, ""));
const files = [...new Set(named)].sort();

// What `npm pack` would put in the tarball, read without running prepack (which would build again).
const [pack] = JSON.parse(
  execFileSync("npm", ["pack", "--dry-run", "--json", "--ignore-scripts"], { cwd: root, encoding: "utf8" }),
);
const packed = new Set(pack.files.map((f) => f.path));

const missing = files.filter((f) => !existsSync(join(root, f)));
const unpacked = files.filter((f) => existsSync(join(root, f)) && !packed.has(f));

for (const f of missing) console.error(`package.json names ${f}, which the build did not write.`);
for (const f of unpacked) console.error(`package.json names ${f}, which npm pack leaves out of the tarball.`);
if (missing.length || unpacked.length) process.exit(1);
console.log(`All ${files.length} files package.json names are built and packed.`);
