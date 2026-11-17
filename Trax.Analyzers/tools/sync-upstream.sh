#!/usr/bin/env bash
# Copies the analyzer rules from Meziantou.Analyzer (MIT, see ../THIRD-PARTY-NOTICES.md) into this
# folder and renames them into Trax's: namespace Meziantou.Analyzer becomes Trax.Analyzers, rule id
# MA0004 becomes TRX0004, and so on. Trax references none of those packages; the copy is Trax's code.
#
#   Trax.Analyzers/tools/sync-upstream.sh            re-copy the versions upstream.txt names
#   Trax.Analyzers/tools/sync-upstream.sh 3.0.330    move to another upstream tag
#
# Only the copied trees are replaced (src/Trax.Analyzers/{Rules,Internals,...}, the code fixers, the
# annotations, the Roslyn helpers, the rule docs and the tests). The build files, the package, the
# profile and the polyfills are Trax's own and are not touched; rerun tools/generate-polyfills.sh if
# the polyfill list in src/Directory.Build.props changes. Review the diff like any other change: a
# rule upstream adds arrives switched off until rules.txt names it.
#
# Pick a tag that has been on nuget.org for at least 7 days (AGENTS.md, Dependabot's cooldown).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
manifest="$here/upstream.txt"

get() { sed -n "s/^$1=//p" "$manifest"; }

tag="${1:-$(get analyzer)}"
helpers="$(get roslyn_helpers)"
[ -n "$tag" ] && [ -n "$helpers" ] || { echo "upstream.txt must name analyzer= and roslyn_helpers=" >&2; exit 1; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "Fetching Meziantou.Analyzer $tag"
git -c advice.detachedHead=false clone -q --depth 1 --branch "$tag" \
  https://github.com/meziantou/Meziantou.Analyzer.git "$work/analyzer"
commit="$(git -C "$work/analyzer" rev-parse HEAD)"

echo "Fetching Meziantou.Framework.Roslyn $helpers"
curl -fsSL -o "$work/helpers.nupkg" \
  "https://api.nuget.org/v3-flatcontainer/meziantou.framework.roslyn/$helpers/meziantou.framework.roslyn.$helpers.nupkg"
mkdir "$work/helpers"
(cd "$work/helpers" && unzip -q ../helpers.nupkg)

up="$work/analyzer"

# Source tree -> destination. Each destination is replaced whole.
replace_tree() {
  local from="$1" to="$2"
  rm -rf "$to"
  mkdir -p "$to"
  cp -R "$from"/. "$to"/
}

# The analyzer: everything but upstream's project and build files.
dest="$here/src/Trax.Analyzers"
for d in Configurations Internals Resources Rules Suppressors; do
  replace_tree "$up/src/Meziantou.Analyzer/$d" "$dest/$d"
done
for f in AssemblyInfo.cs RuleCategories.cs RuleIdentifiers.cs BannedSymbols.txt; do
  cp "$up/src/Meziantou.Analyzer/$f" "$dest/$f"
done

# The code fixers.
dest="$here/src/Trax.Analyzers.CodeFixers"
for d in "$up"/src/Meziantou.Analyzer.CodeFixers/*/; do
  replace_tree "$d" "$dest/$(basename "$d")"
done
find "$up/src/Meziantou.Analyzer.CodeFixers" -maxdepth 1 -name '*.cs' -exec cp {} "$dest/" \;

# The annotations a consumer may put on their own code.
dest="$here/src/Trax.Analyzers.Annotations"
mkdir -p "$dest"
find "$dest" -maxdepth 1 -name '*.cs' -delete
find "$up/src/Meziantou.Analyzer.Annotations" -maxdepth 1 -name '*.cs' -exec cp {} "$dest/" \;
cp "$up/src/Meziantou.Analyzer.Annotations/README.md" "$dest/README.md"

# The Roslyn helpers the rules are written against, a source-only package upstream.
replace_tree "$work/helpers/contentFiles/cs/any/Meziantou.Framework.Roslyn" "$here/src/Trax.Analyzers.Roslyn"

# The tests and their harness.
dest="$here/tests/Trax.Analyzers.Tests"
for d in Harness Helpers Internals Refactorings Rules Suppressors; do
  replace_tree "$up/tests/Meziantou.Analyzer.Test/$d" "$dest/$d"
done
cp "$up/tests/Meziantou.Analyzer.Test/RuleHelpUri.cs" "$dest/RuleHelpUri.cs"

# The rule pages.
replace_tree "$up/docs/Rules" "$here/docs/rules"
cp "$up/docs/generated-code.md" "$here/docs/generated-code.md"

# Upstream's licenses, kept with the copy (MIT requires the notice to travel with it).
cp "$up/LICENSE.txt" "$here/licenses/Meziantou.Analyzer.txt"
cp "$work/helpers/LICENSE.txt" "$here/licenses/Meziantou.Framework.Roslyn.txt"

copied=(
  "$here/src/Trax.Analyzers"
  "$here/src/Trax.Analyzers.CodeFixers"
  "$here/src/Trax.Analyzers.Annotations"
  "$here/src/Trax.Analyzers.Roslyn"
  "$here/tests/Trax.Analyzers.Tests"
  "$here/docs/rules"
  "$here/docs/generated-code.md"
)

# Rule files are named after their id.
find "$here/docs/rules" -name 'MA[0-9][0-9][0-9][0-9].md' | while read -r f; do
  mv "$f" "$(dirname "$f")/TRX${f##*/MA}"
done

# The renames. Order matters: the longest names first. Names that refer to other libraries a
# consumer's code may use (Meziantou.Framework.Assertions and the like) are left alone, since the
# rules look for those types by name; so is the polyfill package, which is not copied.
find "${copied[@]}" -type f \( -name '*.cs' -o -name '*.md' -o -name '*.txt' -o -name '*.json' \) -print0 |
  xargs -0 perl -pi -e '
    s{https://github\.com/meziantou/Meziantou\.Analyzer/blob/main/src/Meziantou\.Analyzer.CodeFixers/}{https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/src/Trax.Analyzers.CodeFixers/}g;
    s{https://github\.com/meziantou/Meziantou\.Analyzer/blob/main/src/Meziantou\.Analyzer\.Annotations/}{https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/src/Trax.Analyzers.Annotations/}g;
    s{https://github\.com/meziantou/Meziantou\.Analyzer/blob/main/src/Meziantou\.Analyzer/}{https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/src/Trax.Analyzers/}g;
    s{https://github\.com/meziantou/Meziantou\.Analyzer/blob/main/docs/Rules/}{https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/}g;
    s{https://github\.com/meziantou/Meziantou\.Analyzer/blob/main/docs/}{https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/}g;
    s/\bMeziantou\.Analyzer\.Annotations\b/Trax.Analyzers.Annotations/g;
    s/\bMeziantou\.Analyzer\.CodeFixers\b/Trax.Analyzers.CodeFixers/g;
    s/\bMeziantou\.Analyzer\.Test\b/Trax.Analyzers.Tests/g;
    s/\bMeziantou\.Analyzer\b/Trax.Analyzers/g;
    s/\bMeziantou\.Framework\.Roslyn\b/Trax.Analyzers.Roslyn/g;
    s/\bMEZIANTOU_ANALYZER_GENERATED_CODE\b/TRAX_ANALYZERS_GENERATED_CODE/g;
    s/\bMEZIANTOU_ANALYZER_ANNOTATIONS\b/TRAX_ANALYZERS_ANNOTATIONS/g;
    s/\bMEZIANTOU_FRAMEWORK_ROSLYN_/TRAX_ANALYZERS_ROSLYN_/g;
    s/\bMA(\d{4})\b/TRX$1/g;
    s/\bMAS(\d{4})\b/TRXS$1/g;
    s/urn:meziantou\.analyzer:/urn:trax.analyzers:/g;
    s{github\.com/meziantou/Trax\.Analyzers/}{github.com/meziantou/Meziantou.Analyzer/}g;
    s/\bAddMeziantouAnnotations\b/AddTraxAnnotations/g;
    s/\bMeziantouIncludeBannedSyntaxesFile\b/TraxIncludeBannedSyntaxesFile/g;
    s/\bMeziantouAnalysisMode\b/TraxAnalysisMode/g;
  '

# The annotations are matched by namespace parts (Trax.Analyzers.Annotations.X).
perl -0pi -e 's/Name: "Analyzer",(\s*)ContainingSymbol: INamespaceSymbol(\s*)\{(\s*)Name: "Meziantou",/Name: "Analyzers",$1ContainingSymbol: INamespaceSymbol$2\{$3Name: "Trax",/g' \
  "$here/src/Trax.Analyzers/Internals/AnnotationAttributes.cs"

# Upstream's test harness names its cache folder after the project; keep it apart from upstream's.
# It also downloads from www.nuget.org, which CI's egress policy does not allow; api.nuget.org's flat
# container serves the same package.
harness="$here/tests/Trax.Analyzers.Tests/Harness/NuGetPackages.cs"
perl -pi -e 's/"Meziantou\.AnalyzerTests"/"Trax.Analyzers.Tests"/g' "$harness"
perl -pi -e 's{https://www\.nuget\.org/api/v2/package/\{packageName\}/\{version\}}{https://api.nuget.org/v3-flatcontainer/{packageName.ToLowerInvariant()}/{version.ToLowerInvariant()}/{packageName.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg}g' "$harness"
grep -q 'api.nuget.org/v3-flatcontainer' "$here/tests/Trax.Analyzers.Tests/Harness/NuGetPackages.cs" ||
  { echo "The harness download URL moved upstream; update the rewrite in this script" >&2; exit 1; }

{
  echo "# What tools/sync-upstream.sh copied, and from where. Written by the script."
  echo "analyzer=$tag"
  echo "analyzer_commit=$commit"
  echo "roslyn_helpers=$helpers"
} > "$manifest"

echo "Copied Meziantou.Analyzer $tag ($commit). Left over upstream names, for review:"
grep -rn 'Meziantou' "${copied[@]}" | grep -v -E 'Meziantou\.Framework\.(Assertions|TemporaryDirectory|StringExtensions|FullPath)|AddMeziantouFramework|Meziantou\.Framework"' || echo "  none"
