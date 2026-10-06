#!/usr/bin/env bash
# Builds the operations dashboard's demo (dashboard/, its own npm project) and puts it in
# public/dashboard/demo, where /dashboard frames it. The demo answers from the recordings committed
# in dashboard/src/demo/data, so building it needs no network beyond the npm install.
#
# The dashboard keeps its own lockfile and installs with it, locked and with no install scripts,
# the same as the site's own install in CI. An install that is already current is reused.
# --include=dev because Vercel builds with NODE_ENV=production, under which npm skips
# devDependencies, and the build's tools (vite, typescript, the plugins) are all of those.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
dashboard="$root/dashboard"
target="$root/public/dashboard/demo"

cd "$dashboard"
if [ ! -f node_modules/.package-lock.json ] || [ package-lock.json -nt node_modules/.package-lock.json ]; then
  npm ci --include=dev --ignore-scripts --no-audit --no-fund
fi
npm run build:demo

rm -rf "$target"
mkdir -p "$(dirname "$target")"
cp -R dist-demo "$target"
echo "dashboard demo: $(du -sh "$target" | cut -f1) in public/dashboard/demo"
