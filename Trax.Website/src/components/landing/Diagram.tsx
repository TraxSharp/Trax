// The pieces the home page's diagrams are drawn with: a box with a label, and an arrow between boxes. They are plain
// HTML, so a diagram reflows on a phone instead of shrinking. Keep a diagram as simple as these allow: a label per
// box, at most a short line under it, and one muted caption below the whole diagram.

type Tone = "plain" | "accent" | "fail" | "muted";

const TONES: Record<Tone, string> = {
  plain: "border-border bg-bg-secondary text-text-primary",
  accent: "border-accent/50 bg-accent/10 text-accent-bright",
  fail: "border-derail/50 bg-derail/10 text-derail",
  muted: "border-dashed border-border bg-transparent text-text-muted",
};

export function Box({
  label,
  note,
  tone = "plain",
  className = "",
}: {
  label: React.ReactNode;
  note?: React.ReactNode;
  tone?: Tone;
  className?: string;
}) {
  return (
    <div className={`flex min-w-0 flex-col justify-center rounded-md border px-3 py-2 text-center ${TONES[tone]} ${className}`}>
      <span className="text-sm font-medium">{label}</span>
      {note && <span className="mt-0.5 text-xs text-text-muted">{note}</span>}
    </div>
  );
}

// An arrow between boxes. "flow" points right from sm up and down below it, so a row of boxes that stacks on a phone
// still reads in order; "down" and "right" point the one way at every width.
export function Arrow({ label, direction = "flow" }: { label?: string; direction?: "flow" | "down" | "right" }) {
  const glyph = direction === "down" ? "↓" : direction === "right" ? "→" : null;
  return (
    <div className={`flex shrink-0 items-center justify-center gap-1.5 text-text-muted ${direction === "right" ? "flex-row" : direction === "down" ? "flex-col" : "flex-col sm:flex-row"}`}>
      {label && <span className="text-xs">{label}</span>}
      {glyph ? (
        <span aria-hidden="true">{glyph}</span>
      ) : (
        <>
          <span aria-hidden="true" className="sm:hidden">
            &darr;
          </span>
          <span aria-hidden="true" className="hidden sm:inline">
            &rarr;
          </span>
        </>
      )}
    </div>
  );
}

// The frame a diagram sits in, with its caption under it.
export function Figure({ children, caption }: { children: React.ReactNode; caption?: React.ReactNode }) {
  return (
    <figure className="mt-8">
      <div className="rounded-lg border border-border/60 bg-bg-secondary/40 p-5 sm:p-8">{children}</div>
      {caption && <figcaption className="mt-3 max-w-2xl text-sm text-text-muted">{caption}</figcaption>}
    </figure>
  );
}
