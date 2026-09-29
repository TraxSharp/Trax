import type { Metadata } from "next";
import { MDXRemote } from "next-mdx-remote/rsc";
import { getAllDocs, getDocBySlug, markdownPath } from "@/lib/docs";
import { docsMdxOptions } from "@/lib/mdx-options";
import { buildNavTree } from "@/lib/nav-tree";
import DocsLayout from "@/components/docs/DocsLayout";
import MarkdownLink from "@/components/docs/MarkdownLink";
import { mdxComponents } from "@/components/mdx/MdxComponents";

export function generateMetadata(): Metadata {
  const doc = getDocBySlug("");
  return {
    title: "Documentation",
    description:
      doc?.description ??
      "Trax documentation — Railway Oriented Programming for .NET",
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
        <div className="docs-content">
          <div className="mb-6 flex justify-end">
            <MarkdownLink slug="" />
          </div>
          <MDXRemote
            source={doc.content}
            components={mdxComponents}
            options={docsMdxOptions}
          />
        </div>
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
