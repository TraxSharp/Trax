import { notFound } from "next/navigation";
import type { Metadata } from "next";
import { MDXRemote } from "next-mdx-remote/rsc";
import {
  documentTitle,
  getAllDocs,
  getDocBySlug,
  generateStaticParams as genParams,
  markdownPath,
} from "@/lib/docs";
import { docsMdxOptions } from "@/lib/mdx-options";
import { buildNavTree } from "@/lib/nav-tree";
import DocsLayout from "@/components/docs/DocsLayout";
import DocsBreadcrumb from "@/components/docs/DocsBreadcrumb";
import PageLinks from "@/components/docs/PageLinks";
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
    title: documentTitle(doc),
    description: doc.description,
    alternates: {
      canonical: `/docs/${slugStr}`,
      types: { "text/markdown": markdownPath(slugStr) },
    },
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
      <div className="mb-6 flex items-start justify-between gap-4">
        <DocsBreadcrumb
          title={doc.title}
          parent={doc.parent}
          grandParent={doc.grandParent}
        />
        <PageLinks doc={doc} />
      </div>
      <div className="docs-content">
        <MDXRemote
          source={doc.body}
          components={mdxComponents}
          options={docsMdxOptions}
        />
      </div>
    </DocsLayout>
  );
}
