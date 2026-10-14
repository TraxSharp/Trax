import type { Metadata } from "next";
import { getAllDocs, getDocBySlug, markdownPath } from "@/lib/docs";
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
  const doc = getDocBySlug("");

  if (!doc) {
    return (
      <div>
        <h1 className="text-3xl font-bold text-text-primary">Documentation</h1>
        <p className="mt-4 text-text-secondary">
          Select a topic from the sidebar to get started.
        </p>
      </div>
    );
  }

  return (
    <>
      <div className="mb-6 flex justify-end">
        <PageLinks doc={doc} />
      </div>
      <DocArticle doc={doc} docs={getAllDocs()} />
    </>
  );
}
