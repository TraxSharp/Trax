export function Pager({
  total,
  isEstimated,
  isCapped,
  isFirstPage,
  nextCursor,
  onPrev,
  onNext,
}: {
  total: number | undefined;
  isEstimated: boolean | undefined;
  // The count stopped at its cap (a text-filtered log count stops at 10,000): at least this many.
  isCapped?: boolean;
  isFirstPage: boolean;
  nextCursor: number | null | undefined;
  onPrev: () => void;
  onNext: (cursor: number) => void;
}) {
  return (
    <div className="flex items-center justify-between mt-4 text-sm">
      <span className="text-muted">
        {total != null ? totalLabel(total, isEstimated, isCapped) : ""}
      </span>
      <div className="flex gap-2">
        <button
          onClick={onPrev}
          disabled={isFirstPage}
          className="px-3 py-1.5 rounded-lg border border-line-strong text-fg-2 disabled:opacity-40 hover:bg-hover"
        >
          Previous
        </button>
        <button
          onClick={() => nextCursor != null && onNext(nextCursor)}
          disabled={nextCursor == null}
          className="px-3 py-1.5 rounded-lg border border-line-strong text-fg-2 disabled:opacity-40 hover:bg-hover"
        >
          Next
        </button>
      </div>
    </div>
  );
}

/** "1,234 total", "~1,234 total" for an estimate, "10,000+ total" for a capped count. */
function totalLabel(total: number, isEstimated?: boolean, isCapped?: boolean): string {
  if (isCapped) return `${total.toLocaleString("en-US")}+ total`;
  return `${isEstimated ? "~" : ""}${total.toLocaleString()} total`;
}
