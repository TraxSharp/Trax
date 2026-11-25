// What `trax deploy` writes from the DistributedWorkers sample's trax.deploy.json, read off
// src/data/deploy-generated.json (made by Trax.Samples' scripts/recordings/deploy-generated.sh and deploy-generated.mjs,
// running the real CLI). The "What you write, what Trax writes" tab shows these; every number comes from here.

export interface HostExport {
  service: string;
  project: string;
  count: number;
  export: {
    roles: string[];
    database: { provider: string; migratesOnStart: boolean } | null;
    transports: string[];
    endpoints: { graphQL?: string; health?: string };
  };
}

export interface GeneratedFile {
  name: string;
  kind: string | null;
  lines: number;
}

export interface DeployGenerated {
  generator: string;
  commit: string;
  dotnet: string;
  file: { path: string; code: string; lines: number };
  hosts: HostExport[];
  targets: {
    kubernetes: {
      command: string;
      exit: number;
      files: GeneratedFile[];
      objects: number;
      lines: number;
      kinds: Record<string, number>;
      hardening: {
        containers: number;
        dropAll: number;
        noPrivilegeEscalation: number;
        seccomp: number;
        readOnlyRoot: number;
        ownUserNamespace: number;
        pods: number;
        secretReferences: number;
      };
      /** The objects whose containers do not all have a read-only root, and what each runs. */
      writableRoot: { file: string; service: string }[];
      log: string[];
    };
    podman: { command: string; exit: number; files: GeneratedFile[]; units: number; lines: number; log: string[] };
    compose: { command: string; exit: number; log: string[] };
    aws: { command: string; change: string; exit: number; log: string[]; bare: { change: string; exit: number; log: string[] } };
  };
}

/** What a host reported it needs, in a few plain words each. */
export function needsOf(host: HostExport): string[] {
  const e = host.export;
  const needs: string[] = [];
  for (const role of e.roles) needs.push(role === "api" ? "serves an API" : role === "scheduler" ? "schedules work" : role === "worker" ? "runs jobs" : role);
  if (e.database) needs.push(e.database.migratesOnStart ? `${e.database.provider === "postgres" ? "Postgres" : e.database.provider}, migrated` : e.database.provider);
  for (const t of e.transports) needs.push(t === "rabbitmq" ? "RabbitMQ" : t);
  if (e.endpoints.health) needs.push(`health at ${e.endpoints.health}`);
  return needs;
}

/** The last line a refused run printed: why trax refused. */
export const refusalOf = (log: string[]): string => log[log.length - 1] ?? "";

/** The Kubernetes objects in the order trax applies them, one entry per kind with how many. */
export function kindsInOrder(g: DeployGenerated): { kind: string; count: number }[] {
  const seen: string[] = [];
  for (const f of g.targets.kubernetes.files) if (f.kind && !seen.includes(f.kind)) seen.push(f.kind);
  return seen.map((kind) => ({ kind, count: g.targets.kubernetes.kinds[kind] ?? 0 }));
}

/** A number as the page writes it: 1,056. */
export const n = (x: number) => x.toLocaleString("en-US");
