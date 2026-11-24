#!/usr/bin/env bash
# Builds the image the sandbox escape tests run, and prints the variables they read.
#
#   Trax.Scheduler/tests/Trax.Scheduler.Sandbox.Image/build-escape-image.sh
#
# Publishes this project in the pinned SDK image (nothing runs on the host), builds
# localhost/trax-sandbox-escape:test from Containerfile, and prints its digest-pinned reference
# and the holder's (the ASP.NET base image, run with `sleep infinity`). See
# Trax.Docs/sdk-reference/scheduler-api/trax-sandbox-launcher.md, "Escape tests".
set -euo pipefail

here=$(dirname "$(readlink -f "$0")")
repo=$(git -C "$here" rev-parse --show-toplevel)
sdk=localhost/trax-test:10.0.301
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

podman run --rm --security-opt label=disable -v "$repo:/w:ro" -v "$work:/out" "$sdk" sh -c '
  cp -r /w /src && cd /src &&
  dotnet publish Trax.Scheduler/tests/Trax.Scheduler.Sandbox.Image -c Release -o /out/app -v q -nologo'
cp "$here/Containerfile" "$work/Containerfile"
podman build -q -t localhost/trax-sandbox-escape:test "$work" >/dev/null

digest() { podman image inspect --format '{{.Digest}}' "$1"; }
echo "TRAX_SANDBOX_IMAGE=localhost/trax-sandbox-escape@$(digest localhost/trax-sandbox-escape:test)"
echo "TRAX_SANDBOX_HOLDER_IMAGE=mcr.microsoft.com/dotnet/aspnet@$(digest mcr.microsoft.com/dotnet/aspnet:10.0)"
