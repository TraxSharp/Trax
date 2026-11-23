#!/usr/bin/env bash
# Writes src/Trax.Analyzers.Polyfills/: the newer BCL members the copied rules call, as source, so
# the analyzers build for netstandard2.0 (the target every compiler and IDE loads). Upstream gets
# them from the Meziantou.Polyfill source generator; this runs that generator once, in a throwaway
# podman container, and keeps its output. Nothing in the build references the generator.
#
#   Trax.Analyzers/tools/generate-polyfills.sh
#
# The list is the one upstream keeps in its root Directory.Build.props, copied into polyfills.txt
# beside this script. Rerun after a sync that changes it.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version=1.0.169
sdk="$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' "$here/../global.json" | head -1)"
image="mcr.microsoft.com/dotnet/sdk:$sdk"
list="$(grep -v '^#' "$here/tools/polyfills.txt" | grep -v '^$' | tr '\n' ';')"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

cat > "$work/Polyfills.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>generated</CompilerGeneratedFilesOutputPath>
    <MeziantouPolyfill_IncludedPolyfills>$list</MeziantouPolyfill_IncludedPolyfills>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Meziantou.Polyfill" Version="$version" PrivateAssets="all" />
    <!-- What the analyzers build against: it brings System.Memory, so the Span polyfills are generated too. -->
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" />
  </ItemGroup>
</Project>
EOF
echo 'internal static class Empty { }' > "$work/Empty.cs"

podman run --rm -e NUGET_PACKAGES=/work/packages -v "$work:/work:Z" -w /work "$image" \
  dotnet build -c Release -p:ManagePackageVersionsCentrally=false >/dev/null

out="$here/src/Trax.Analyzers.Polyfills"
rm -rf "$out"
mkdir -p "$out"
find "$work/generated" -name '*.g.cs' ! -name 'Debug.g.cs' -print0 |
  while IFS= read -r -d '' f; do
    # The generator names files after documentation ids; keep the member name, drop the signature.
    n="$(basename "$f" .g.cs | sed 's/[(].*//; s/[^A-Za-z0-9._]/_/g')"
    m="$n.cs"
    i=2
    while [ -e "$out/$m" ]; do m="$n.$i.cs"; i=$((i + 1)); done
    cp "$f" "$out/$m"
  done
# The generator's own feature switches, renamed with the rest of the copy.
perl -pi -e 's/\bMEZIANTOU_POLYFILL_/TRAX_POLYFILL_/g' "$out"/*.cs
cp "$(find "$work/packages/meziantou.polyfill" -iname 'LICENSE*' | head -1)" "$here/licenses/Meziantou.Polyfill.txt"
echo "Wrote $(ls "$out" | wc -l) polyfills from Meziantou.Polyfill $version"
