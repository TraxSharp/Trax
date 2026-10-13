import type { Metadata } from "next";
import { getAllDocs, getDocBySlug, markdownPath } from "@/lib/docs";
import { buildNavTree } from "@/lib/nav-tree";
import DocsLayout from "@/components/docs/DocsLayout";
import PageLinks from "@/components/docs/PageLinks";
import DocArticle from "@/components/docs/DocArticle";

export function generateMetadata(): Metadata {
  const doc = getDocBySlug("");
  return {
    title: "Documentation",
    description:
      doc?.description ??
      "Trax .NET documentation: a headless web framework for .NET.",
    alternates: {
      canonical: "/docs",
      ...(doc ? { types: { "text/markdown": markdownPath("") } } : {}),
    },
  };
}

export default function DocsHomePage() {
  const allDocs = getAllDocs();
  const navTree = buildNavTree(allDocs);
  const doc = getDocBySlug("");

  return (
    <DocsLayout navTree={navTree}>
      {doc ? (
        <>
          <div className="mb-6 flex justify-end">
            <PageLinks doc={doc} />
          </div>
          <DocArticle doc={doc} docs={allDocs} />
        </>
      ) : (
        <div>
          <h1 className="text-3xl font-bold text-text-primary">
            Documentation
          </h1>
          <p className="mt-4 text-text-secondary">
            Select a topic from the sidebar to get started.
          </p>
        </div>
      )}
    </DocsLayout>
  );
}
