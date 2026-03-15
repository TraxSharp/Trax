import type { Metadata } from "next";
import { MDXRemote } from "next-mdx-remote/rsc";
import remarkGfm from "remark-gfm";
import rehypeSlug from "rehype-slug";
import rehypePrettyCode from "rehype-pretty-code";
import { getAllDocs, getDocBySlug } from "@/lib/docs";
import { buildNavTree } from "@/lib/nav-tree";
import DocsLayout from "@/components/docs/DocsLayout";
import { mdxComponents } from "@/components/mdx/MdxComponents";

export const metadata: Metadata = {
  title: "Documentation",
  description: "Trax documentation — Railway Oriented Programming for .NET",
};

export default function DocsHomePage() {
  const allDocs = getAllDocs();
  const navTree = buildNavTree(allDocs);
  const doc = getDocBySlug("");

  return (
    <DocsLayout navTree={navTree}>
      {doc ? (
        <div className="docs-content">
          <MDXRemote
            source={doc.content}
            components={mdxComponents}
            options={{
              mdxOptions: {
                remarkPlugins: [remarkGfm],
                rehypePlugins: [
                  rehypeSlug,
                  [
                    rehypePrettyCode,
                    {
                      theme: "github-dark-dimmed",
                      keepBackground: false,
                    },
                  ],
                ],
              },
            }}
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
