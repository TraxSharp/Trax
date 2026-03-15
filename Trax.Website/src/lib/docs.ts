import fs from "fs";
import path from "path";
import matter from "gray-matter";

export interface DocPage {
  slug: string;
  title: string;
  content: string;
  navOrder: number;
  parent?: string;
  grandParent?: string;
  hasChildren: boolean;
  section?: string;
  filePath: string;
}

const DOCS_DIR = path.join(process.cwd(), ".docs-cache");

function getSlugFromPath(filePath: string): string {
  const relative = path.relative(DOCS_DIR, filePath);
  // index.md at root becomes empty string (docs home)
  if (relative === "index.md") return "";
  // Remove .md extension
  const slug = relative.replace(/\.md$/, "");
  return slug;
}

function transformJekyllLinks(content: string): string {
  // Transform {{ site.baseurl }}{% link path/to/file.md %} -> /docs/path/to/file
  // Also handles optional #anchor suffix
  return content.replace(
    /\{\{\s*site\.baseurl\s*\}\}\{%\s*link\s+([\w/.-]+\.md)\s*%\}(#[\w-]*)?/g,
    (_match, filePath: string, anchor: string | undefined) => {
      const slug = filePath.replace(/\.md$/, "");
      // index.md -> /docs
      const href = slug === "index" ? "/docs" : `/docs/${slug}`;
      return href + (anchor || "");
    }
  );
}

function escapeMdxOutsideCodeBlocks(content: string): string {
  // MDX chokes on JSX-like angle brackets (e.g. C# generics) outside code fences.
  // Escape them only in non-code-block lines.
  const lines = content.split("\n");
  let inCodeBlock = false;
  const result: string[] = [];

  for (const line of lines) {
    if (line.trimStart().startsWith("```")) {
      inCodeBlock = !inCodeBlock;
      result.push(line);
      continue;
    }

    if (inCodeBlock) {
      result.push(line);
    } else {
      // Also skip inline code spans — don't escape inside backticks
      // Replace angle brackets that look like generics or HTML-like tokens
      // but preserve actual HTML tags we want (like <br>, <details>, etc.)
      let escaped = line;
      // Escape < that aren't part of well-known HTML tags or markdown links
      escaped = escaped.replace(
        /`[^`]*`/g,
        (match) => match // preserve inline code as-is by replacing back
      );
      // For lines not fully inside inline code, escape bare angle brackets
      // Strategy: replace inline code with placeholders, escape, restore
      const placeholders: string[] = [];
      escaped = escaped.replace(/`[^`]*`/g, (match) => {
        placeholders.push(match);
        return `%%INLINECODE${placeholders.length - 1}%%`;
      });
      // Now escape angle brackets in the non-code parts
      escaped = escaped.replace(/</g, "&lt;").replace(/>/g, "&gt;");
      // Restore inline code
      escaped = escaped.replace(
        /%%INLINECODE(\d+)%%/g,
        (_, i) => placeholders[parseInt(i)]
      );
      result.push(escaped);
    }
  }

  return result.join("\n");
}

function transformBlockIALs(content: string): string {
  // Transform {: .note } / {: .warning } / {: .important } block IALs
  // into simple bold markers (MDX components can style these)
  return content
    .replace(/\{:\s*\.note\s*\}/g, "")
    .replace(/\{:\s*\.warning\s*\}/g, "")
    .replace(/\{:\s*\.important\s*\}/g, "")
    .replace(/\{:\s*\.highlight\s*\}/g, "");
}

function getAllMarkdownFiles(dir: string): string[] {
  const files: string[] = [];
  if (!fs.existsSync(dir)) return files;

  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      files.push(...getAllMarkdownFiles(fullPath));
    } else if (entry.name.endsWith(".md")) {
      files.push(fullPath);
    }
  }
  return files;
}

export function getAllDocs(): DocPage[] {
  const files = getAllMarkdownFiles(DOCS_DIR);
  return files.map((filePath) => {
    const raw = fs.readFileSync(filePath, "utf-8");
    const { data, content } = matter(raw);
    const transformed = escapeMdxOutsideCodeBlocks(
      transformBlockIALs(transformJekyllLinks(content))
    );
    return {
      slug: getSlugFromPath(filePath),
      title: data.title || path.basename(filePath, ".md"),
      content: transformed,
      navOrder: data.nav_order ?? 999,
      parent: data.parent,
      grandParent: data.grand_parent,
      hasChildren: data.has_children ?? false,
      section: data.section,
      filePath,
    };
  });
}

export function getDocBySlug(slug: string): DocPage | undefined {
  const docs = getAllDocs();
  return docs.find((doc) => doc.slug === slug);
}

export function generateStaticParams(): { slug: string[] }[] {
  const docs = getAllDocs();
  return docs
    .filter((doc) => doc.slug !== "") // docs home handled by /docs/page.tsx
    .map((doc) => ({
      slug: doc.slug.split("/"),
    }));
}
