// Small inline spinner for busy buttons, so an in-progress action shows local feedback in addition
// to the global top-of-page progress bar.
export function Spinner({ className = "" }: { className?: string }) {
  return (
    <svg
      className={`animate-spin h-3.5 w-3.5 ${className}`}
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden="true"
    >
      <circle
        className="opacity-25"
        cx="12"
        cy="12"
        r="10"
        stroke="currentColor"
        strokeWidth="4"
      />
      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
    </svg>
  );
}
