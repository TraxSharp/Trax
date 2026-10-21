// Back and Next under a replay's other controls. A replay plays itself until one is pressed, then moves only when told.
export default function StepButtons({ onBack, onNext, at, of }: { onBack: () => void; onNext: () => void; at: number; of: number }) {
  const button =
    "flex-1 rounded-md border border-border bg-bg-tertiary px-3 py-2 text-sm text-text-primary transition-colors hover:border-accent/60 disabled:cursor-default disabled:opacity-35 disabled:hover:border-border";
  return (
    <div className="flex items-center gap-2">
      <button onClick={onBack} disabled={at <= 0} className={button}>
        Back
      </button>
      <button onClick={onNext} disabled={at >= of} className={button}>
        Next
      </button>
      <span className="w-12 flex-none text-right font-mono text-[11px] text-text-muted">
        {at} / {of}
      </span>
    </div>
  );
}
