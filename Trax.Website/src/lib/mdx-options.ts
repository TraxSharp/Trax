import remarkGfm from "remark-gfm";
import rehypeSlug from "rehype-slug";
import rehypePrettyCode from "rehype-pretty-code";
import type { MDXRemoteProps } from "next-mdx-remote/rsc";

/**
 * MDX options shared by every docs page. A fence with no language is
 * highlighted as `text`, so every block carries a `data-language` and the
 * `code` component can tell it apart from inline code.
 */
export const docsMdxOptions: MDXRemoteProps["options"] = {
  mdxOptions: {
    remarkPlugins: [remarkGfm],
    rehypePlugins: [
      rehypeSlug,
      [
        rehypePrettyCode,
        {
          theme: "github-dark-dimmed",
          keepBackground: false,
          defaultLang: { block: "text" },
        },
      ],
    ],
  },
};
