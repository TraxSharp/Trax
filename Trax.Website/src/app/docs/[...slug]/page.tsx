import { notFound } from "next/navigation";
import type { Metadata } from "next";
import { MDXRemote } from "next-mdx-remote/rsc";
import remarkGfm from "remark-gfm";
import rehypeSlug from "rehype-slug";
import rehypePrettyCode from "rehype-pretty-code";
import { getAllDocs, getDocBySlug, generateStaticParams as genParams } from "@/lib/docs";
import { buildNavTree } from "@/lib/nav-tree";
import DocsLayout from "@/components/docs/DocsLayout";
import DocsBreadcrumb from "@/components/docs/DocsBreadcrumb";
import { mdxComponents } from "@/components/mdx/MdxComponents";

interface PageProps {
  params: Promise<{ slug: string[] }>;
}

export async function generateStaticParams() {
  return genParams();
}

export async function generateMetadata({
  params,
}: PageProps): Promise<Metadata> {
  const { slug } = await params;
  const slugStr = slug.join("/");
  const doc = getDocBySlug(slugStr);
  if (!doc) return { title: "Not Found" };
  return {
    title: doc.title,
    description: `Trax documentation: ${doc.title}`,
  };
}

export default async function DocPage({ params }: PageProps) {
  const { slug } = await params;
  const slugStr = slug.join("/");
  const doc = getDocBySlug(slugStr);

  if (!doc) {
    notFound();
  }

  const allDocs = getAllDocs();
  const navTree = buildNavTree(allDocs);

  return (
    <DocsLayout navTree={navTree}>
      <DocsBreadcrumb
        title={doc.title}
        parent={doc.parent}
        grandParent={doc.grandParent}
      />
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
    </DocsLayout>
  );
}
