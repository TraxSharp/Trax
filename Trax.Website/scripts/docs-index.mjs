// Reads the front matter of the synced docs and writes what the site needs at build
// time without parsing every page again:
//
//   redirects.json     [{ source, destination, permanent }] from each page's redirect_from,
//                      read by next.config.ts
//   source-kinds.json  { "<source path>": "file" | "dir" | "missing" } for each page's
//                      source entries, so a Source link is a GitHub blob or tree URL
//
// Usage: node scripts/docs-index.mjs <docs cache dir> <repository root>

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import matter from "gray-matter";

function listMarkdown(dir) {
  return fs
    .readdirSync(dir, { recursive: true, encoding: "utf-8" })
    .filter((f) => f.endsWith(".md"))
    .map((f) => f.split(path.sep).join("/"))
    .sort();
}

function asList(value) {
  if (typeof value === "string") return value.trim() ? [value.trim()] : [];
  if (Array.isArray(value)) {
    return value.filter((v) => typeof v === "string" && v.trim()).map((v) => v.trim());
  }
  return [];
}

/** The site path of a cached page: index.md is /docs, a/b.md is /docs/a/b. */
export function pagePath(file) {
  const slug = file.replace(/\.md$/, "");
  return slug === "index" ? "/docs" : `/docs/${slug}`;
}

/** A redirect_from entry as a site path: a leading slash, no trailing slash or `.md`. */
export function normalizeRedirect(entry) {
  const withSlash = entry.startsWith("/") ? entry : `/${entry}`;
  return withSlash.replace(/\.md$/, "").replace(/(.)\/+$/, "$1");
}

/**
 * The permanent redirects the pages ask for. Throws when an old path is also a
 * live page (the redirect would hide the page) or is claimed by two pages.
 *
 * @param {{ file: string, data: Record<string, unknown> }[]} pages
 */
export function collectRedirects(pages) {
  const live = new Set(pages.map((p) => pagePath(p.file)));
  const claimed = new Map();
  const redirects = [];
  const problems = [];
  for (const { file, data } of pages) {
    const destination = pagePath(file);
    for (const entry of asList(data.redirect_from)) {
      const source = normalizeRedirect(entry);
      if (live.has(source)) {
        problems.push(`${file}: redirect_from ${source} is a published page`);
        continue;
      }
      if (claimed.has(source)) {
        if (claimed.get(source) !== file) {
          problems.push(`${file}: redirect_from ${source} is also claimed by ${claimed.get(source)}`);
        }
        continue;
      }
      claimed.set(source, file);
      redirects.push({ source, destination, permanent: true });
    }
  }
  if (problems.length > 0) throw new Error(`bad redirect_from:\n  ${problems.join("\n  ")}`);
  return redirects.sort((a, b) => a.source.localeCompare(b.source));
}

/**
 * Whether each source entry names a file or a directory in the checkout.
 *
 * @param {{ data: Record<string, unknown> }[]} pages
 * @param {string} repoRoot
 */
export function collectSourceKinds(pages, repoRoot) {
  const kinds = {};
  for (const { data } of pages) {
    for (const entry of asList(data.source)) {
      if (entry in kinds) continue;
      try {
        const stat = fs.statSync(path.join(repoRoot, entry));
        kinds[entry] = stat.isDirectory() ? "dir" : "file";
      } catch {
        kinds[entry] = "missing";
      }
    }
  }
  return kinds;
}

export function readPages(cacheDir) {
  return listMarkdown(cacheDir).map((file) => ({
    file,
    data: matter(fs.readFileSync(path.join(cacheDir, file), "utf-8")).data,
  }));
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  const [cacheDir, repoRoot] = process.argv.slice(2);
  if (!cacheDir || !repoRoot) {
    console.error("usage: node scripts/docs-index.mjs <docs cache dir> <repository root>");
    process.exit(2);
  }
  const pages = readPages(cacheDir);
  try {
    const redirects = collectRedirects(pages);
    fs.writeFileSync(path.join(cacheDir, "redirects.json"), `${JSON.stringify(redirects, null, 2)}\n`);
    fs.writeFileSync(
      path.join(cacheDir, "source-kinds.json"),
      `${JSON.stringify(collectSourceKinds(pages, repoRoot), null, 2)}\n`
    );
    console.log(`Wrote ${redirects.length} redirects`);
  } catch (error) {
    console.error(`error: ${error.message}`);
    process.exit(1);
  }
}
