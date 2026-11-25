# shellcheck shell=bash
# shellcheck disable=SC2034 # the variables are read by cluster.sh, which sources this
# Sourced by cluster.sh: sets REGISTRY_IMAGE, BUSYBOX and K3S_VERSION from the Dockerfile beside it, which Dependabot
# keeps current.
pins_file=$(dirname "${BASH_SOURCE[0]}")/Dockerfile
pin() { sed -n "s|^FROM \(docker\.io/$1:[^ ]*\) AS .*|\1|p" "$pins_file"; }
REGISTRY_IMAGE=$(pin library/registry)
BUSYBOX=$(pin library/busybox)
K3S_VERSION=$(pin rancher/k3s | sed 's/.*://; s/-k3s/+k3s/')
for pinned in REGISTRY_IMAGE BUSYBOX K3S_VERSION; do
  [ -n "${!pinned}" ] || { echo "FAIL: $pins_file pins no image for $pinned" >&2; exit 1; }
done
unset -f pin
unset pins_file pinned
