import { getBundles } from "@/lib/llms";

export const dynamic = "force-static";
export const dynamicParams = false;

export function generateStaticParams() {
  return getBundles().map((bundle) => ({ bundle: bundle.file }));
}

export async function GET(
  _request: Request,
  { params }: { params: Promise<{ bundle: string }> }
) {
  const { bundle: file } = await params;
  const bundle = getBundles().find((b) => b.file === file);
  if (!bundle) return new Response("Not found", { status: 404 });
  return new Response(bundle.text, {
    headers: { "Content-Type": "text/plain; charset=utf-8" },
  });
}
