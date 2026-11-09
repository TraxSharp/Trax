#!/usr/bin/env bash
# Runs the CheckIn sample: the database, the host and the page. Open http://localhost:5281 twice, sign in as a passenger in one
# window and a gate agent in the other. From the Trax.Samples folder:   samples/CheckIn/demo.sh
set -euo pipefail
cd "$(dirname "$0")/../.."

docker compose up -d database
dotnet run --project samples/CheckIn/Trax.Samples.CheckIn.Api &
host=$!
trap 'kill $host 2>/dev/null || true' EXIT

(cd samples/CheckIn/Trax.Samples.CheckIn.Web && npm ci --ignore-scripts && npm run dev)
