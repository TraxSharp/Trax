import { getAllDocs, getDocBySlug, markdownSlug } from "@/lib/docs";

// Serves /docs/<slug>.md (rewritten here in next.config.ts): the page's source
// markdown from Trax.Docs with the Jekyll front matter removed. The front matter
// only carries sidebar plumbing (layout, nav_order, parent), and the title is
// already the page's H1.

export const dynamic = "force-static";
export const dynamicParams = false;

export function generateStaticParams() {
  return getAllDocs().map((doc) => ({
    slug: markdownSlug(doc.slug).split("/"),
  }));
}

export async function GET(
  _request: Request,
  { params }: { params: Promise<{ slug: string[] }> }
) {
  const { slug } = await params;
  const joined = slug.join("/");
  const doc = getDocBySlug(joined === "index" ? "" : joined);
  if (!doc) return new Response("Not found", { status: 404 });
  return new Response(doc.body.trimStart(), {
    headers: { "Content-Type": "text/markdown; charset=utf-8" },
  });
}
