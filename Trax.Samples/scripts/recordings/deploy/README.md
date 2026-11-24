# Deploy recordings

The home page's deploy replay (Trax.Website's `DeployReplay.tsx`) plays real `trax deploy` runs: one per shape an app
can take, on a Kubernetes cluster (k3s) and on one machine with the podman runtime. `../deploy.sh` makes them and
`../deploy.mjs` writes the website's copy, `Trax.Website/src/data/deploy-recordings.json`.

| Shape | Folder | What it deploys |
|---|---|---|
| All-in-one | `all-in-one/` | the `trax-hub` template: API, scheduler and the jobs in one process |
| API + Scheduler | `api-scheduler/` | the `trax-api` and `trax-scheduler` templates as two services on one database |
| Hub + Workers | `../../../samples/DistributedWorkers/` | the DistributedWorkers sample, with its own `trax.deploy.json` |
| Just the scheduler | `scheduler/` | the `trax-scheduler` template |

Ephemeral dispatch (Lambda) has no folder: `trax deploy` does not deploy a Lambda function, and the page says so. AWS
has no recording either: there is no account to record against, and the page says that too.

Each scenario, in order, starting from what the one before left running:

| Scenario | What the recorder does | What it records |
|---|---|---|
| `first-deploy` | a fresh namespace (or podman stack) and its Secret, then `trax deploy` | the CLI's lines, every pod or container, the health probe from the start |
| `release-while-busy` | puts one finished run of the app's own workflow back to InProgress, deploys the same file again, and ends the run by hand 15 seconds after the drain first reports it busy | the same, and the run's state and the drain twice a second |
| `broken-release` | deploys with `Kestrel__Endpoints__Http__Url` set to `not-a-url`, which every service refuses when it starts serving (the migration and the drain serve nothing, so they pass) | the same; on podman a minute after the restart, then the working version is put back unrecorded |
| `who-can-reach-what` | deploys with the probed service held to `"egress": ["api.github.com"]` and the egress image built from the same commit | the deploy, the NetworkPolicy (or, on podman, what is published) checks and `trax-egress check` run in the held pod as the app |

A shape whose file holds its services to their egress (DistributedWorkers: `"egress": []`) gets the egress image built
from the same commit named in it, since a development build of trax names none. Under podman its first three
scenarios deploy it without the egress, as `.github/ci/deploy/podman.sh` does: a first deploy held to its egress did
not get its migration to Postgres there when this was recorded (the migration exited on "Resource temporarily
unavailable"). `who-can-reach-what` then deploys it held, onto the Postgres already running.

The copies of the file a scenario changes are made by editing its text (so the page shows it as written) and are
checked against the same change made with `jq` before they are deployed. The page says, under the file, what a
scenario changed.

## Recording

Use a throwaway VM, never your own machine: the recorder installs nothing, but it deploys, deletes namespaces and
podman stacks, and runs code from the tree. What the recordings in the website were made on:

1. A Debian 13 cloud image (`debian-13-genericcloud-amd64`), 6 vCPU, 10 GB, a 60 GB overlay, booted with
   `qemu-system-x86_64 -enable-kvm` and user networking, with a cloud-init seed giving one user passwordless sudo,
   lingering (`loginctl enable-linger`), the `docker` group, and the packages `podman docker.io curl jq git uidmap
   slirp4netns passt nftables catatonit zstd libicu76 dbus-user-session openssl`.
2. The guest kept off the host and the LAN: an nftables table `inet lock` dropping output and forwarded traffic to
   `10.0.2.2`, `192.168.0.0/16` and `100.64.0.0/10` (after `ct state established,related accept`, so SSH stays up).
3. The .NET SDK the repository's `global.json` names, through `dotnet-install.sh`.
4. For `kubernetes`: a registry on `127.0.0.1:5001` and k3s, installed the way `.github/ci/deploy/cluster.sh` does
   with `K8S=k3s` (the same pinned installer digest), with `KUBECONFIG` pointing at a copy of its kubeconfig.
5. For `podman`: `~/.config/containers/registries.conf` marking `localhost:5001` insecure, so the egress image
   pushed there can be pulled by digest.

Then, from a clone of the repository in the VM. `trax deploy` tags each image with the commit, and `-dirty` when the
tree has changes. Building a project for its host's report and then publishing it for linux-x64 rewrites the
`net10.0/linux-x64` section of every `packages.lock.json` it touches back and forth (no version changes), which would
make every tag `-dirty`; in the throwaway clone, tell git to leave those files alone first:

```bash
git ls-files -z '*packages.lock.json' | xargs -0 git update-index --skip-worktree
Trax.Samples/scripts/recordings/deploy.sh all-in-one kubernetes
Trax.Samples/scripts/recordings/deploy.sh all-in-one podman
# ... and api-scheduler, scheduler, hub-workers
```

A shape on one target takes about fifteen minutes; the broken release on Kubernetes alone takes five, since a
Deployment is given five minutes to make progress. Copy `Trax.Samples/scripts/recordings/out/deploy/` back, then:

```bash
cd Trax.Samples/scripts/recordings
node deploy.mjs --copy-to ../../../Trax.Website/src/data
```

and run the website's `npm test`, whose `tests/deploy-replay.test.ts` checks every claim the page's copy makes against
the recording.
