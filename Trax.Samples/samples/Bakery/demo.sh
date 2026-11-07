#!/usr/bin/env bash
# Runs the Bakery: the database, the host and the page. Open http://localhost:5281 twice, sign in as a customer in one
# window and a baker in the other. From the Trax.Samples folder:   samples/Bakery/demo.sh
set -euo pipefail
cd "$(dirname "$0")/../.."

docker compose up -d database
dotnet run --project samples/Bakery/Trax.Samples.Bakery.Api &
host=$!
trap 'kill $host 2>/dev/null || true' EXIT

(cd samples/Bakery/Trax.Samples.Bakery.Web && npm ci --ignore-scripts && npm run dev)
