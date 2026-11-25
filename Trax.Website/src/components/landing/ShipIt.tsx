import CodeFold from "./CodeFold";
import DemoSection from "./DemoSection";
import DeployReplay from "./DeployReplay";
import ShipItTabs from "./ShipItTabs";

// Deploying with trax, in three tabs (ShipItTabs): what trax writes from your one file, one release while people use
// the app, and who can reach whom once it runs, each read off generated or recorded data. Every recorded deploy, with
// its file, its terminal and its checks, is in the fold under them (DeployReplay), and so is how they were made.
export default function ShipIt() {
  return (
    <DemoSection
      title="Ship a new version without anyone noticing"
      intro={
        <p>
          You write one short file. <code className="font-mono text-[0.9em]">trax deploy</code> reads what your code needs, writes
          what the cluster or the machine runs, and ships each new version beside the old one once the work in progress is done.
          A version that never gets ready is put back on its own.
        </p>
      }
      links={[
        { href: "/docs/reference/deploy-a-trax-app", label: "Deploy a Trax app" },
        { href: "/docs/reference/deployment-topologies", label: "The shapes an app can take" },
      ]}
      after={
        <div className="mt-4 flex flex-col gap-4">
          <p className="max-w-3xl text-sm leading-relaxed text-text-secondary">
            On Kubernetes, only the app&apos;s own services reach its database and each one reaches only the domains you name; on AWS,
            security groups keep the database to the app&apos;s tasks. Code a person or an agent wrote can run in a sandbox with no network.
          </p>
          <CodeFold label="Every recorded deploy: each shape, Kubernetes and one machine, with the file, the terminal and the checks">
            <DeployReplay />
          </CodeFold>
          <CodeFold label="How these were recorded, and how to record them again">
            <div className="max-w-3xl space-y-3 text-sm leading-relaxed text-text-secondary">
              <p>
                <span className="font-mono text-xs">Trax.Samples/scripts/recordings/deploy.sh</span> ran each deploy for real in a throwaway
                virtual machine: k3s for Kubernetes, rootless podman under systemd for one machine. It wrote down every line trax printed,
                every pod or container, a request to the app five times a second, and the network checks. Nothing on this page is typed in
                by hand. The picture above is the Hub + Workers release on Kubernetes, with its clock shortened.
              </p>
              <p>
                The first tab&apos;s counts come from <span className="font-mono text-xs">Trax.Samples/scripts/recordings/deploy-generated.sh</span>,
                which runs <span className="font-mono text-xs">trax deploy plan</span> on the DistributedWorkers sample in the .NET SDK container
                and counts what it wrote.
              </p>
              <p>
                For a release while busy, the recorder puts one finished run back to running so the deploy has something to wait for,
                and ends it by hand. The broken release sets a web address every service refuses at start.
              </p>
              <p>
                AWS is not recorded: there is no account to record against yet. Its picture is simulated from the steps the ECS deploy
                takes in the code (a drain task first, the new task started beside the old one, the circuit breaker rolling back a
                version that never gets healthy), timed like the Kubernetes recording, and it counts no requests.
              </p>
              <pre className="whitespace-pre-wrap rounded border border-border bg-bg-primary p-3 font-mono text-[11.5px] leading-relaxed [overflow-wrap:anywhere]">
                {[
                  "# in the VM, from the repository root",
                  "Trax.Samples/scripts/recordings/deploy.sh hub-workers kubernetes",
                  "Trax.Samples/scripts/recordings/deploy.sh hub-workers podman",
                  "# then, with the out/ folder copied back",
                  "cd Trax.Samples/scripts/recordings && node deploy.mjs --copy-to ../../../Trax.Website/src/data",
                ].join("\n")}
              </pre>
            </div>
          </CodeFold>
        </div>
      }
    >
      <ShipItTabs />
    </DemoSection>
  );
}
