import type { Metadata } from "next";
import { getAllPosts } from "@/lib/blog";
import BlogCard from "@/components/blog/BlogCard";

export const metadata: Metadata = {
  title: "Blog",
};

export default function BlogPage() {
  const posts = getAllPosts();

  return (
    <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6 lg:px-8">
      <h1 className="text-3xl font-bold text-text-primary">Blog</h1>
      <p className="mt-2 text-text-secondary">
        Updates, tutorials, and insights from the Trax project.
      </p>

      {posts.length === 0 ? (
        <div className="mt-8 rounded-xl border border-border bg-bg-card p-8">
          <p className="text-text-muted">No posts yet. Check back soon.</p>
        </div>
      ) : (
        <div className="mt-8 space-y-4">
          {posts.map((post) => (
            <BlogCard
              key={post.slug}
              slug={post.slug}
              title={post.title}
              date={post.date}
              description={post.description}
              tags={post.tags}
            />
          ))}
        </div>
      )}
    </div>
  );
}
