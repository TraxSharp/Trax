// The strip of controls across the top of every "See it running" demo, with the line saying what just happened under
// them, inside the same box. Each demo puts its run below, so the five tabs share one shape.
export default function ReplayToolbar({
  children,
  caption,
  hint,
  reserve = "min-h-[3em] lg:min-h-[1.5em]",
}: {
  children: React.ReactNode;
  caption: React.ReactNode;
  // What to do, shown quietly while the demo has nothing to say, so the strip is never an empty bar.
  hint?: React.ReactNode;
  // The caption's height before anything is in it, as tall as its longest line wraps, so the run below never moves.
  reserve?: string;
}) {
  return (
    <div className="rounded-lg border border-border bg-bg-secondary text-sm">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2.5 p-3">{children}</div>
      <div className={`flex flex-wrap items-center gap-2 border-t border-border px-3 py-2.5 leading-normal ${caption ? "text-signal" : "text-text-muted"} ${reserve}`}>
        {caption || hint}
      </div>
    </div>
  );
}

export function PrimaryButton(props: React.ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button
      {...props}
      className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-accent-ink transition-colors hover:bg-accent-hover disabled:cursor-default disabled:opacity-40"
    />
  );
}

export function ToolButton(props: React.ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button
      {...props}
      className="rounded-md border border-border bg-bg-tertiary px-3 py-2 text-left text-sm text-text-primary transition-colors hover:border-accent/60 disabled:cursor-default disabled:opacity-35 disabled:hover:border-border"
    />
  );
}

export function ToolSelect({
  label,
  ...props
}: { label: string } & React.SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <label className="flex items-center gap-2 text-xs text-text-muted">
      {label}
      <select
        {...props}
        className="max-w-full rounded-md border border-border bg-bg-primary px-2 py-2 text-sm text-text-primary outline-none focus:border-accent disabled:opacity-50"
      />
    </label>
  );
}

// The run's milestones, lit as it reaches them, at the far end of the strip.
export function ToolbarPills({ children }: { children: React.ReactNode }) {
  return (
    <ol className="flex gap-1.5 text-xs font-medium sm:ml-auto" aria-label="Progress">
      {children}
    </ol>
  );
}
