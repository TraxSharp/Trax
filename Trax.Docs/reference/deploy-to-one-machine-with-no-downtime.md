---
title: Deploy to one machine with no downtime
description: "Run k3s in a virtual machine on one host and deploy a Trax app to it with trax deploy, so each release rolls without an outage."
type: how-to
area: tooling
nav_order: 7
---

# Deploy to one machine with no downtime

The podman runtime (`"runtime": "podman"`) runs an app on one host under systemd, and each release
stops a service before its new version starts. For a release with no outage on one machine, run a
real cluster on it: k3s, inside a virtual machine, and deploy to it with the kubernetes target, the
same way you would deploy to EKS. Kubernetes brings the new copy up beside the old one and moves
traffic over once it is ready.
[A zero-downtime deploy on k3s](/docs/reference/a-zero-downtime-deploy-on-k3s) walks the same path step by step,
with the output at each one.

Use a virtual machine, not k3s installed on the host. k3s runs as root, so a container that escaped
would be root on the machine your keys and other services live on. Inside a VM it is root in the VM,
which holds only the app. Every Trax pod already runs with `hostUsers: false`, all capabilities
dropped and a read-only root, so an escape is hard either way; the VM is what limits what one could
reach.

## What you need

- A host with KVM and room for a VM of about 4 vCPU, 8 GB of memory and 40 GB of disk. k3s itself
  takes 0.5 to 1 GB at idle.
- A Linux guest with a kernel of 6.3 or later, for `hostUsers: false` (Debian 13 and Ubuntu 24.04
  are both new enough).
- A registry the cluster can pull from. The steps below run one inside the VM.

## 1. Install k3s in the VM

```bash
# in the VM, as root
curl -sfL https://get.k3s.io | INSTALL_K3S_VERSION=v1.36.6+k3s1 sh -s - --disable traefik
kubectl get nodes
```

Pin `INSTALL_K3S_VERSION` and move it on purpose: a k3s upgrade restarts the control plane.
`--disable traefik` leaves out the ingress controller k3s installs by default; the Tailscale
operator is what exposes the app (step 4).

## 2. A registry inside the VM

`trax deploy` pushes the images it builds, and the cluster pulls them. A registry on the VM's
loopback is enough for one machine:

```bash
# in the VM
podman run -d --name registry --restart=always -p 127.0.0.1:5001:5000 \
  docker.io/library/registry:3@sha256:82f6ff4a7f982960c8aa51617fea3aabc485df6e55cce80d22deb1de36f1bc3c
cat > /etc/rancher/k3s/registries.yaml <<'EOF'
mirrors:
  "localhost:5001":
    endpoint: ["http://127.0.0.1:5001"]
EOF
systemctl restart k3s
```

From the machine you deploy from, reach the registry and the API server over SSH:

```bash
ssh -N -L 5001:127.0.0.1:5001 -L 6443:127.0.0.1:6443 you@the-vm &
scp you@the-vm:/etc/rancher/k3s/k3s.yaml ~/.kube/the-vm.yaml   # readable by root only in the VM
export KUBECONFIG=~/.kube/the-vm.yaml
kubectl get nodes
```

## 3. Deploy

Point an environment at the cluster and the registry:

```json
"home": {
  "target": "kubernetes",
  "profile": "try",
  "registry": "localhost:5001",
  "database": { "inCluster": true }
}
```

Create the namespace and its Secret, then deploy, as for any cluster
([`--target kubernetes`](/docs/reference/cli#--target-kubernetes)):

```bash
kubectl create namespace orders-home
kubectl -n orders-home create secret generic orders-home --from-env-file=orders-home.env
trax deploy --env home
```

`trax deploy` checks the context it is about to use, applies to it, drains the hosts already running,
waits for each service, and rolls every service back to the version before if one never gets ready.

## 4. Reach it on the tailnet

Nothing on the cluster is published to the host's network. Expose the app on your tailnet with
`"exposure": "tailnet"` (see [`--target kubernetes`](/docs/reference/cli#--target-kubernetes)), which installs
nothing on the host.

## What this does not give you

- One machine is still one machine: a host that goes down takes the app with it.
- The in-cluster Postgres (`"inCluster": true`) is one copy with no backups, for a `try`
  environment. A `production` environment names a database you run and back up.

## SDK Reference

> [TraxMigrate](/docs/sdk-reference/configuration/trax-migrate) | [DeployManifest](/docs/sdk-reference/configuration/deploy-manifest)
