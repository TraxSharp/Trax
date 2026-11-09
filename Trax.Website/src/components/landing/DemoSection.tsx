import Link from "next/link";
import SampleSource, { type SampleSourceProps } from "./SampleSource";

// A home page section that shows one sample: its heading and a short paragraph, the sample's demo in place of a
// diagram, then where the sample's code is. Wider than the text-only sections, so a demo fits a laptop screen.
export default function DemoSection({
  title,
  intro,
  links,
  source,
  children,
  after,
}: {
  title: string;
  intro: React.ReactNode;
  links: { href: string; label: string }[];
  /** Where the sample is; left out while the sample is not written yet. */
  source?: SampleSourceProps;
  children: React.ReactNode;
  /** Anything that reads after the demo, such as what the demo does not show. */
  after?: React.ReactNode;
}) {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-7xl px-4 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">{title}</h2>
        <div className="mt-3 max-w-3xl space-y-3 text-text-secondary">{intro}</div>
        <div className="mt-4 flex flex-wrap gap-x-6 gap-y-2 text-sm font-medium">
          {links.map((l) => (
            <Link key={l.href} href={l.href} className="text-accent hover:text-accent-hover">
              {l.label} &rarr;
            </Link>
          ))}
        </div>
        <div className="mt-8">{children}</div>
        {source && <SampleSource {...source} />}
        {after}
      </div>
    </section>
  );
}
