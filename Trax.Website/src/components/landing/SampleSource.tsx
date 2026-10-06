import Link from "next/link";

const REPO = "https://github.com/TraxSharp/Trax.Samples";

export interface SampleSourceProps {
  /** The sample's folder under samples/ in Trax.Samples, such as "Recovery". */
  folder: string;
  /** The files a reader should open first, relative to the sample's folder. */
  files: { path: string; label: string }[];
  /** The sample's page under traxsharp.net/docs. */
  docs: string;
  /** The commands that start it, run from the Trax.Samples root. */
  run: string[];
}

/**
 * Under every sample on the landing page: where its real C# lives and how to start it yourself. The demo above it
 * is a recording; this is the code that made it.
 */
export default function SampleSource({ folder, files, docs, run }: SampleSourceProps) {
  const tree = `${REPO}/tree/main/samples/${folder}`;
  // A file's name, or its folder and name when another listed file has the same name (two Program.cs).
  const nameOf = (path: string) => {
    const parts = path.split("/");
    const name = parts[parts.length - 1];
    const shared = files.filter((f) => f.path.endsWith(`/${name}`) || f.path === name).length > 1;
    return shared && parts.length > 1 ? parts.slice(-2).join("/") : name;
  };
  return (
    <div className="mt-4 grid gap-4 rounded-lg border border-border bg-bg-secondary p-4 text-sm lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
      <div>
        <p className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">The real C#</p>
        <ul className="mt-2 space-y-1">
          {files.map((f) => (
            <li key={f.path} className="flex flex-wrap items-baseline gap-x-2">
              <a
                href={`${REPO}/blob/main/samples/${folder}/${f.path}`}
                target="_blank"
                rel="noopener noreferrer"
                className="font-mono text-xs text-accent hover:text-accent-hover"
              >
                {nameOf(f.path)}
              </a>
              <span className="text-xs text-text-muted">{f.label}</span>
            </li>
          ))}
        </ul>
        <div className="mt-3 flex flex-wrap gap-x-5 gap-y-1">
          <a
            href={tree}
            target="_blank"
            rel="noopener noreferrer"
            className="font-medium text-accent hover:text-accent-hover"
          >
            samples/{folder} on GitHub &rarr;
          </a>
          <Link href={docs} className="text-text-secondary hover:text-text-primary">
            Read the sample &rarr;
          </Link>
        </div>
      </div>
      <div className="min-w-0">
        <p className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">Run it yourself</p>
        <pre className="mt-2 whitespace-pre-wrap rounded border border-border bg-bg-primary p-3 font-mono text-[11.5px] leading-relaxed text-text-secondary [overflow-wrap:anywhere]">
          {[`git clone ${REPO}.git && cd Trax.Samples`, ...run].map((line) => `$ ${line}`).join("\n")}
        </pre>
      </div>
    </div>
  );
}
