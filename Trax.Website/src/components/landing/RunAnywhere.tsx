import Link from "next/link";
import { Arrow, Box, Figure } from "./Diagram";

// What every deploy does, wherever it goes. Each line says only what ships, and what the deploy-cluster CI job proves
// on a real cluster.
const guarantees: { title: string; body: string }[] = [
  {
    title: "Releases without an outage",
    body: "Before a release, every process stops starting new work and the deploy waits for what is running. On a cluster or AWS, the new version then starts beside the old one.",
  },
  {
    title: "Rolls back on its own",
    body: "On a cluster or AWS, a release that never gets ready puts every service back on the version it ran before.",
  },
  {
    title: "Private",
    body: "Serve the API only on your tailnet, with sign-in by tailnet identity. Inside the cluster, only the app's own services reach its database.",
  },
  {
    title: "Only where it needs to go",
    body: "Each service names the domains it may reach, and nothing else gets out. Memory and CPU per service, and a read-only filesystem for all of them.",
  },
  {
    title: "Untrusted code in a sandbox",
    body: "Code a person or an agent wrote runs in a fresh virtual machine per run, with no network at all.",
  },
  {
    title: "Proved on a real cluster",
    body: "Every change to the deploy code deploys a real app to a real cluster, measures a release for downtime, and rolls a broken one back.",
  },
];

const targets: { label: string; note: string }[] = [
  { label: "Your own machine", note: "podman, or k3s in a VM" },
  { label: "AWS", note: "ECS or EKS" },
  { label: "Any Kubernetes cluster", note: "your own, or a cloud's" },
];

export default function RunAnywhere() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-7xl px-4 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">Run it anywhere</h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          One file says where the app runs. The same file deploys it to your own machine, to AWS, or to any Kubernetes
          cluster, and each process says what it needs, so the file and the code cannot disagree.
        </p>
        <div className="mt-4 flex flex-wrap gap-x-6 gap-y-2">
          <Link href="/docs/reference/deploy-a-trax-app" className="text-sm font-medium text-accent hover:text-accent-hover">
            Deploy a Trax app &rarr;
          </Link>
          <Link
            href="/docs/reference/deploy-to-one-machine-with-no-downtime"
            className="text-sm font-medium text-accent hover:text-accent-hover"
          >
            One machine, no downtime &rarr;
          </Link>
        </div>

        <Figure caption="trax deploy builds the images, migrates the database and rolls each service, on whichever target the environment names.">
          <div className="flex flex-col items-stretch gap-3 sm:flex-row sm:items-center">
            <Box label="trax.deploy.json" tone="accent" className="sm:w-48" />
            <Arrow label="trax deploy" />
            <div className="flex flex-1 flex-col gap-2">
              {targets.map((t) => (
                <Box key={t.label} label={t.label} note={t.note} />
              ))}
            </div>
          </div>
        </Figure>

        <div className="mt-10 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {guarantees.map((g) => (
            <div key={g.title} className="rounded-lg border border-border/60 bg-bg-secondary/40 p-5">
              <h3 className="font-semibold text-text-primary">{g.title}</h3>
              <p className="mt-1.5 text-sm leading-relaxed text-text-secondary">{g.body}</p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
