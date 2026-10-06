import { useEffect } from "react";
import type { ReactNode } from "react";

// Base modal overlay. Closes on backdrop click and Escape.
export function Modal({
  title,
  onClose,
  children,
  wide = false,
}: {
  title: string;
  onClose: () => void;
  children: ReactNode;
  // A wider card for forms with a document editor.
  wide?: boolean;
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div
      className="fixed inset-0 z-40 flex items-center justify-center bg-black/40 p-4"
      onClick={onClose}
    >
      <div
        className={`w-full ${wide ? "max-w-2xl" : "max-w-md"} max-h-[90vh] overflow-y-auto rounded-xl bg-surface shadow-xl border border-line`}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="px-5 py-4 border-b border-line">
          <h2 className="text-lg font-semibold text-fg">
            {title}
          </h2>
        </div>
        <div className="px-5 py-4">{children}</div>
      </div>
    </div>
  );
}
