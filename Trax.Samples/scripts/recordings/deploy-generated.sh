#!/usr/bin/env bash
# Writes what `trax deploy` generates from the DistributedWorkers sample's trax.deploy.json, for the home page's
# "What you write, what Trax writes" tab: what each host reports with --trax-export-deploy, the Kubernetes manifests
# (`trax deploy plan --env cluster`), the podman runtime's units and pods (`trax deploy plan --env home`), and what
# compose and AWS say about the same file. Nothing is deployed and no image is built. deploy-generated.mjs turns it
# into the website's copy, Trax.Website/src/data/deploy-generated.json.
#
# Run it from the repository root in the .NET SDK container, never on your machine (it builds and runs the sample):
#
#   podman run --rm -v "$PWD":/w -w /w mcr.microsoft.com/dotnet/sdk:10.0.301 \
#     bash -c 'apt-get update -qq && apt-get install -y -qq jq >/dev/null && Trax.Samples/scripts/recordings/deploy-generated.sh'
#
# The tree must not hold the dashboard's node_modules (Trax.Api.Dashboard then wants node to build its app); a fresh
# clone does not. What it changes to get there, and why:
# - The plans name an egress image by a placeholder digest (all zeros): the sample holds its services to their
#   egress, and a development build of trax names no egress image, so it refuses to plan without one. The digest
#   appears in the manifests as written; it changes no object and no count.
# - For AWS it adds an "aws" environment to a copy of the file. trax refuses the sample's file there twice over: first
#   because ECS cannot hold a service to its egress, then, with the egress left out of the copy, because the hosts
#   use RabbitMQ, which an AWS stack does not provide yet. Both refusals are kept, as printed.
set -euo pipefail

HERE=Trax.Samples/scripts/recordings
DIR=Trax.Samples/samples/DistributedWorkers
FILE=$DIR/trax.deploy.json
OUT=$PWD/$HERE/out/generated
PLACEHOLDER=ghcr.io/traxsharp/trax-egress@sha256:0000000000000000000000000000000000000000000000000000000000000000
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

rm -rf "$OUT"
mkdir -p "$OUT"
cleanup() { rm -rf "$DIR"/trax.deploy.gen*.json "$DIR/.trax"; }
trap cleanup EXIT

dotnet build Trax.Cli/src/Trax.Cli/Trax.Cli.csproj -c Release -v quiet -nologo >/dev/null
TRAX=(dotnet "$PWD/Trax.Cli/src/Trax.Cli/bin/Release/net10.0/trax.dll")
cp "$FILE" "$OUT/trax.deploy.json"
git rev-parse HEAD > "$OUT/commit.txt" 2>/dev/null || echo unknown > "$OUT/commit.txt"
dotnet --version > "$OUT/dotnet.txt"

# What each host reports, run the way trax runs it (Trax.Cli/src/Trax.Cli/Deploy/DotnetHostExporter.cs): Release,
# no launch profile, Production, and a placeholder for every connection string, setting and secret the file names.
placeholders=(
  "ASPNETCORE_ENVIRONMENT=Production"
  "ConnectionStrings__$(jq -r '.connectionStrings.trax // "Trax"' "$FILE")=Host=trax-deploy-export.invalid;Database=trax;Username=trax;Password=trax"
  "ConnectionStrings__$(jq -r '.connectionStrings.rabbitMq // "RabbitMq"' "$FILE")=amqps://trax:trax@trax-deploy-export.invalid:5671"
)
while read -r s; do placeholders+=("$s=https://trax-deploy-export.invalid"); done < <(jq -r '.settings[]?' "$FILE")
while read -r s; do placeholders+=("$s=dHJheC1kZXBsb3ktZXhwb3J0LXBsYWNlaG9sZGVyLXNlY3JldA=="); done < <(jq -r '.secrets[]?' "$FILE")
for service in $(jq -r '.services | keys_unsorted[]' "$FILE"); do
  project=$DIR/$(jq -r --arg s "$service" '.services[$s].project' "$FILE")
  env -u DOTNET_ENVIRONMENT "${placeholders[@]}" \
    dotnet run --project "$project" -c Release --no-launch-profile -- --trax-export-deploy "$OUT/export-$service.json" >/dev/null
done

# The copies the plans read, beside the file since its projects are named relative to it.
jq --arg i "$PLACEHOLDER" '{version, app, egressImage: $i} + del(.version, .app)' "$FILE" > "$DIR/trax.deploy.gen.json"
jq '.environments = ({aws: {target: "aws", profile: "production", region: "us-east-1", domain: "energyhub.example.com",
    settings: .environments.cluster.settings}} + .environments)' "$FILE" > "$DIR/trax.deploy.gen-aws.json"
jq 'del(.services[].egress)' "$DIR/trax.deploy.gen-aws.json" > "$DIR/trax.deploy.gen-aws-bare.json"

# Each run's output and exit code, and what it wrote.
run() {
  local name=$1
  shift
  set +e
  (cd "$DIR" && "${TRAX[@]}" "$@") > "$OUT/$name.log" 2>&1
  echo $? > "$OUT/$name.exit"
  set -e
}
run kubernetes deploy plan --env cluster --file trax.deploy.gen.json
cp -r "$DIR/.trax/deploy/kubernetes/$(jq -r .app "$FILE")-cluster" "$OUT/kubernetes"
run podman deploy plan --env home --file trax.deploy.gen.json
cp -r "$DIR/.trax/deploy/podman/$(jq -r .app "$FILE")-home" "$OUT/podman"
run compose deploy --target compose --file trax.deploy.gen.json --out "$OUT/compose"
run aws deploy plan --env aws --file trax.deploy.gen-aws.json
run aws-bare deploy plan --env aws --file trax.deploy.gen-aws-bare.json
echo "wrote $OUT"
