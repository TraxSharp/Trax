"use client";

import { useEffect, useRef, useState } from "react";
import CodeFold from "./CodeFold";
import data from "@/data/deploy-generated.json";
import { kindsInOrder, n, needsOf, refusalOf, type DeployGenerated } from "@/lib/deploy-generated";
import { HUE, alpha } from "@/lib/hue";

// What you write and what trax writes from it, for the DistributedWorkers sample: each host's report of what it needs,
// the short trax.deploy.json, and what `trax deploy` generated for each target, all read off
// src/data/deploy-generated.json (Trax.Samples' scripts/recordings/deploy-generated.sh runs the real CLI; rerun it
// and deploy-generated.mjs when the sample, its file or what trax writes changes).
const g = data as unknown as DeployGenerated;
const k8s = g.targets.kubernetes;
const podman = g.targets.podman;
const aws = g.targets.aws;
const kinds = kindsInOrder(g);
const h = k8s.hardening;

// When each part appears, in milliseconds from the start of the loop.
const T = { hosts: 300, need: 350, file: 3800, deploy: 5600, kind: 380, podman: 10200, aws: 11400, point: 12600, end: 17000 };
const needs = g.hosts.map(needsOf);
const NAMES: Record<string, string> = { database: "Postgres", rabbitmq: "RabbitMQ" };
// Where each host's needs start in the sequence of chips.
const needStart = needs.map((_, i) => needs.slice(0, i).reduce((sum, list) => sum + list.length, 0));

export default function WhatYouWrite() {
  const [at, setAt] = useState(0);
  const [playing, setPlaying] = useState(false);
  const base = useRef(0);
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!playing) return;
    const timer = setInterval(() => setAt((performance.now() - base.current) % T.end), 60);
    return () => clearInterval(timer);
  }, [playing]);

  // Plays, and loops, once it is on screen; with less motion asked for, it shows everything at once.
  useEffect(() => {
    const node = root.current;
    if (!node || typeof IntersectionObserver === "undefined") return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (!entry.isIntersecting) return;
        observer.disconnect();
        base.current = performance.now();
        setPlaying(true);
      },
      { threshold: 0.3 },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, []);

  // Before it first plays (and for a reader who prefers less motion) the whole picture shows.
  const now = playing ? at : T.end - 1;
  const shown = (t: number) => now >= t;
  // A count that runs up to its value as its part appears.
  const upTo = (value: number, from: number) => Math.round(value * Math.min(1, Math.max(0, (now - from) / 1400)));

  return (
    <div ref={root} className="flex flex-col gap-4">
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm text-text-secondary">
          The DistributedWorkers sample: a hub and two workers. What each part needs comes from its code; where it runs comes from the
          file.
        </p>
        <button
          onClick={() => {
            if (playing) setPlaying(false);
            else {
              base.current = performance.now() - (at >= T.end - 1 ? 0 : at);
              setPlaying(true);
            }
          }}
          className="flex-none rounded-md border border-border bg-bg-tertiary px-3 py-1.5 text-sm text-text-primary hover:border-accent/60"
        >
          {playing ? "Pause" : "Play"}
        </button>
      </div>

      <div className="grid items-stretch gap-3 lg:grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)_auto_minmax(0,1.4fr)]">
        <Column title="Your code says what it needs">
          {g.hosts.map((host, i) => (
            <div key={host.service} className="rounded-md border border-border bg-bg-primary p-3">
              <p className="text-sm font-semibold text-text-primary">
                {host.service}
                {host.count > 1 && <span className="font-normal text-text-muted"> ×{host.count}</span>}
              </p>
              <ul className="mt-2 flex flex-wrap gap-1.5">
                {needs[i].map((need, j) => (
                  <Chip key={need} on={shown(T.hosts + (needStart[i] + j) * T.need)}>
                    {need}
                  </Chip>
                ))}
              </ul>
            </div>
          ))}
          <p className="text-xs text-text-muted">
            Each reports it when trax runs it with <span className="font-mono">--trax-export-deploy</span>.
          </p>
        </Column>

        <Arrow on={shown(T.file)} label="" />

        <Column title="You write one file">
          <div className={`flex flex-1 flex-col rounded-md border bg-bg-primary transition-colors duration-500 ${shown(T.file) ? "border-accent/60" : "border-border"}`}>
            <p className="border-b border-border px-3 py-2 font-mono text-xs text-text-secondary">trax.deploy.json</p>
            <pre className="relative max-h-[230px] flex-1 overflow-hidden px-3 py-2 font-mono text-[10.5px] leading-[1.5] text-text-secondary">
              {g.file.code}
              <span className="pointer-events-none absolute inset-x-0 bottom-0 h-16 bg-gradient-to-t from-bg-primary to-transparent" />
            </pre>
          </div>
          <Big on={shown(T.file)} value={n(upTo(g.file.lines, T.file))} unit="lines you wrote" />
        </Column>

        <Arrow on={shown(T.deploy)} label="trax deploy" />

        <Column title="trax writes the rest">
          <Target name="Kubernetes" on={shown(T.deploy)} color={HUE.green}>
            <p className="text-sm text-text-primary">
              <span className="font-mono">{n(upTo(k8s.objects, T.deploy))}</span> objects,{" "}
              <span className="font-mono">{n(upTo(k8s.lines, T.deploy))}</span> lines of YAML
            </p>
            <ul className="mt-2 flex flex-wrap gap-1.5">
              {kinds.map((k, i) => (
                <Chip key={k.kind} on={shown(T.deploy + 600 + i * T.kind)}>
                  {k.count > 1 ? `${k.count} ` : ""}
                  {k.kind}
                </Chip>
              ))}
            </ul>
            <p className={`mt-2 text-xs leading-relaxed transition-opacity duration-500 ${shown(T.deploy + 600 + kinds.length * T.kind) ? "opacity-100" : "opacity-0"} text-text-secondary`}>
              {h.dropAll === h.containers && h.noPrivilegeEscalation === h.containers ? `Every one of its ${h.containers}` : `${h.dropAll} of its ${h.containers}`}{" "}
              containers drops every capability and cannot gain privileges; {h.readOnlyRoot} have a read-only root
              {k8s.writableRoot.length > 0 && ` (${k8s.writableRoot.map((f) => NAMES[f.service] ?? f.service).join(" and ")} keep theirs writable)`}. Passwords and keys
              come from a Secret by reference, never written into the files.
            </p>
          </Target>
          <Target name="Your machine (podman)" on={shown(T.podman)} color={HUE.green}>
            <p className="text-sm text-text-primary">
              <span className="font-mono">{podman.units}</span> systemd units and their pods, <span className="font-mono">{n(upTo(podman.lines, T.podman))}</span>{" "}
              lines
            </p>
          </Target>
          <Target name="AWS" on={shown(T.aws)} color={HUE.redSoft}>
            <p className="text-sm text-text-primary">Refused, before anything is created.</p>
            <p className="mt-1 text-xs leading-relaxed text-text-secondary">The hub&apos;s code connects to RabbitMQ, which ECS cannot run for it yet.</p>
          </Target>
        </Column>
      </div>

      <div
        className={`rounded-lg border p-4 transition-opacity duration-700 ${shown(T.point) ? "opacity-100" : "opacity-0"}`}
        style={{ borderColor: alpha(HUE.green, 40), background: alpha(HUE.green, 6) }}
      >
        <p className="text-text-primary">
          You wrote {n(g.file.lines)} lines. trax wrote {n(k8s.lines)} for Kubernetes and {n(podman.lines)} for one machine, from what your
          code says it needs, so the file and the code cannot disagree.
        </p>
        <p className="mt-2 text-sm text-text-secondary">
          When they would, trax stops before it changes anything. Ask for AWS and it says:
        </p>
        <p className="mt-1 font-mono text-xs leading-relaxed [overflow-wrap:anywhere]" style={{ color: HUE.redSoft }}>
          {refusalOf(aws.bare.log)}
        </p>
      </div>

      <CodeFold label="Every file trax wrote for this sample, and what it refused">
        <div className="grid gap-4 text-sm lg:grid-cols-2">
          <FileList title={`Kubernetes: ${k8s.command}`} files={k8s.files} total={k8s.lines} />
          <FileList title={`Your machine: ${podman.command}`} files={podman.files} total={podman.lines} />
          <div className="space-y-3 lg:col-span-2">
            <Refusal title={`compose: ${g.targets.compose.command}`} text={refusalOf(g.targets.compose.log)} />
            <Refusal title={`AWS: ${aws.command} (${aws.change.toLowerCase()})`} text={refusalOf(aws.log)} />
            <Refusal title={`AWS again (${aws.bare.change.toLowerCase()})`} text={refusalOf(aws.bare.log)} />
            <p className="text-xs text-text-muted">
              Written by <span className="font-mono">{g.generator}</span> from commit {g.commit.slice(0, 12)} with .NET {g.dotnet}. The plans
              name a placeholder egress image, since a development build of trax names none; it changes no count. Run it again in the .NET
              SDK container, then <span className="font-mono">node deploy-generated.mjs --copy-to ../../../Trax.Website/src/data</span>.
            </p>
          </div>
        </div>
      </CodeFold>
    </div>
  );
}

function Column({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="flex min-w-0 flex-col gap-2">
      <p className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">{title}</p>
      {children}
    </div>
  );
}

function Chip({ on, children }: { on: boolean; children: React.ReactNode }) {
  return (
    <li
      className={`rounded-full border px-2 py-0.5 text-xs transition-all duration-500 ${on ? "border-accent/50 text-accent-bright opacity-100" : "translate-y-1 border-border text-text-muted opacity-0"}`}
    >
      {children}
    </li>
  );
}

function Arrow({ on, label }: { on: boolean; label: string }) {
  return (
    <div className={`flex items-center justify-center gap-1.5 text-text-muted transition-opacity duration-500 lg:flex-col ${on ? "opacity-100" : "opacity-30"}`}>
      {label && <span className="font-mono text-xs text-accent-bright">{label}</span>}
      <span aria-hidden="true" className="lg:hidden">
        &darr;
      </span>
      <span aria-hidden="true" className="hidden lg:inline">
        &rarr;
      </span>
    </div>
  );
}

function Big({ on, value, unit }: { on: boolean; value: string; unit: string }) {
  return (
    <p className={`transition-opacity duration-500 ${on ? "opacity-100" : "opacity-0"}`}>
      <span className="font-mono text-2xl text-text-primary">{value}</span> <span className="text-sm text-text-muted">{unit}</span>
    </p>
  );
}

function Target({ name, on, color, children }: { name: string; on: boolean; color: string; children: React.ReactNode }) {
  return (
    <div
      className={`rounded-md border bg-bg-primary p-3 transition-all duration-500 ${on ? "opacity-100" : "translate-x-2 opacity-0"}`}
      style={{ borderColor: on ? alpha(color, 50) : undefined }}
    >
      <p className="mb-1 text-sm font-semibold" style={{ color }}>
        {name}
      </p>
      {children}
    </div>
  );
}

function FileList({ title, files, total }: { title: string; files: { name: string; kind: string | null; lines: number }[]; total: number }) {
  return (
    <div className="rounded-md border border-border bg-bg-primary">
      <p className="border-b border-border px-3 py-2 font-mono text-xs text-text-secondary">{title}</p>
      <ul className="px-3 py-2 font-mono text-[11px] leading-relaxed">
        {files.map((f) => (
          <li key={f.name} className="flex gap-3">
            <span className="min-w-0 flex-1 truncate text-text-secondary" title={f.name}>
              {f.name}
            </span>
            <span className="w-12 flex-none text-right text-text-muted">{f.lines}</span>
          </li>
        ))}
        <li className="mt-1 flex gap-3 border-t border-border pt-1">
          <span className="flex-1 text-text-secondary">{files.length} files</span>
          <span className="w-12 flex-none text-right text-text-primary">{n(total)}</span>
        </li>
      </ul>
    </div>
  );
}

function Refusal({ title, text }: { title: string; text: string }) {
  return (
    <div>
      <p className="font-mono text-xs text-text-secondary">{title}</p>
      <p className="mt-1 font-mono text-xs leading-relaxed [overflow-wrap:anywhere]" style={{ color: HUE.redSoft }}>
        {text}
      </p>
    </div>
  );
}
