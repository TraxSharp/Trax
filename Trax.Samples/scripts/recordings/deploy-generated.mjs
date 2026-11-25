// Writes the website's copy of what deploy-generated.sh wrote: one compact deploy-generated.json for the home page's
// "What you write, what Trax writes" tab. Every count in it is counted here from the files trax wrote; nothing is
// typed in.
//
//   node deploy-generated.mjs --copy-to ../../../Trax.Website/src/data
//
// What it leaves out: the manifests themselves (their names, kinds and line counts stay), the image tags in the
// Jobs' names (the time and commit of the run that made them), and the path inside the container the CLI printed.
import { existsSync, readFileSync, readdirSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, "out", "generated");
const args = process.argv.slice(2);
const copyTo = args.includes("--copy-to") ? args[args.indexOf("--copy-to") + 1] : null;
if (!copyTo || !existsSync(out)) {
  console.error("usage: node deploy-generated.mjs --copy-to <Trax.Website/src/data>, after deploy-generated.sh");
  process.exit(1);
}

const read = (f) => readFileSync(join(out, f), "utf8");
const lineCount = (text) => text.replace(/\n$/, "").split("\n").length;
const count = (texts, re) => texts.reduce((n, t) => n + (t.match(re) ?? []).length, 0);
// The lines trax printed, without the path it wrote to inside the container and without the run's image tag.
const logOf = (name) =>
  read(`${name}.log`)
    .split("\n")
    .filter((l) => l.trim().length > 0)
    .map((l) => l.replace(/ \/w\/\S+/g, "").replace(/-\d{8}-\d{6}-[0-9a-f]{12}(-dirty)?/g, "-<tag>"));
const exitOf = (name) => Number(read(`${name}.exit`).trim());

function files(dir) {
  return readdirSync(join(out, dir))
    .sort()
    .map((name) => {
      const text = read(join(dir, name));
      return { name: name.replace(/-\d{8}-\d{6}-[0-9a-f]{12}(-dirty)?/, "-<tag>"), kind: text.match(/^kind: (\S+)/m)?.[1] ?? null, lines: lineCount(text), text };
    });
}

const file = read("trax.deploy.json");
const parsed = JSON.parse(file);
const hosts = Object.keys(parsed.services).map((service) => ({
  service,
  project: parsed.services[service].project,
  count: parsed.services[service].count ?? 1,
  export: JSON.parse(read(`export-${service}.json`)),
}));

const k8s = files("kubernetes");
const k8sTexts = k8s.map((f) => f.text);
const kinds = {};
for (const f of k8s) if (f.kind) kinds[f.kind] = (kinds[f.kind] ?? 0) + 1;
const podman = files("podman");

const result = {
  generator: "Trax.Samples/scripts/recordings/deploy-generated.sh",
  commit: read("commit.txt").trim(),
  dotnet: read("dotnet.txt").trim(),
  file: { path: "Trax.Samples/samples/DistributedWorkers/trax.deploy.json", code: file, lines: lineCount(file) },
  hosts,
  targets: {
    kubernetes: {
      command: "trax deploy plan --env cluster",
      exit: exitOf("kubernetes"),
      files: k8s.map(({ name, kind, lines }) => ({ name, kind, lines })),
      objects: k8s.filter((f) => f.kind).length,
      lines: k8s.reduce((n, f) => n + f.lines, 0),
      kinds,
      hardening: {
        containers: count(k8sTexts, /^\s+image: /gm),
        dropAll: count(k8sTexts, /^\s+- ALL$/gm),
        noPrivilegeEscalation: count(k8sTexts, /allowPrivilegeEscalation: false/g),
        seccomp: count(k8sTexts, /seccompProfile:/g),
        readOnlyRoot: count(k8sTexts, /readOnlyRootFilesystem: true/g),
        ownUserNamespace: count(k8sTexts, /hostUsers: false/g),
        pods: count(k8sTexts, /automountServiceAccountToken: false/g),
        secretReferences: count(k8sTexts, /secretKeyRef:/g),
      },
      // The objects whose containers do not all have a read-only root, by the name of what they run.
      writableRoot: k8s
        .filter((f) => count([f.text], /^\s+image: /gm) > count([f.text], /readOnlyRootFilesystem: true/g))
        .map((f) => ({ file: f.name, service: f.text.match(/app\.kubernetes\.io\/name: (\S+)/)?.[1] ?? f.name })),
      log: logOf("kubernetes"),
    },
    podman: {
      command: "trax deploy plan --env home",
      exit: exitOf("podman"),
      files: podman.map(({ name, kind, lines }) => ({ name, kind, lines })),
      units: podman.filter((f) => f.name.endsWith(".kube")).length,
      lines: podman.reduce((n, f) => n + f.lines, 0),
      log: logOf("podman"),
    },
    compose: { command: "trax deploy --target compose", exit: exitOf("compose"), log: logOf("compose") },
    aws: {
      command: "trax deploy plan --env aws",
      change: "An aws environment added to a copy of the file.",
      exit: exitOf("aws"),
      log: logOf("aws"),
      bare: {
        change: "The same copy with the services' egress left out.",
        exit: exitOf("aws-bare"),
        log: logOf("aws-bare"),
      },
    },
  },
};

mkdirSync(copyTo, { recursive: true });
writeFileSync(join(copyTo, "deploy-generated.json"), JSON.stringify(result) + "\n");
console.log(`wrote ${join(copyTo, "deploy-generated.json")}`);
