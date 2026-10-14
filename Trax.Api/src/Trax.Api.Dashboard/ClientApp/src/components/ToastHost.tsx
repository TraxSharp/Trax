import { useSyncExternalStore } from "react";
import { dismiss, getToasts, subscribeToasts } from "../lib/toast";
import type { ToastKind } from "../lib/toast";
import { CircleCheck, CircleX, Info, TriangleAlert, X, type LucideIcon } from "lucide-react";

const STYLES: Record<ToastKind, string> = {
  success: "bg-ok-soft border-ok-line text-ok-fg",
  error: "bg-danger-soft border-danger-line text-danger-fg",
  info: "bg-info-soft border-info-line text-info-fg",
  warning: "bg-warn-soft border-warn-line text-warn-fg",
};

const ICONS: Record<ToastKind, LucideIcon> = {
  success: CircleCheck,
  error: CircleX,
  info: Info,
  warning: TriangleAlert,
};

export function ToastHost() {
  const toasts = useSyncExternalStore(subscribeToasts, getToasts, getToasts);

  if (toasts.length === 0) return null;

  return (
    <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2 w-80">
      {toasts.map((t) => {
        const Icon = ICONS[t.kind];
        return (
        <div
          key={t.id}
          role="status"
          className={`flex items-start gap-3 px-4 py-3 rounded-lg border shadow-lg text-sm ${STYLES[t.kind]}`}
        >
          <Icon className="size-4 shrink-0 mt-0.5" />
          <span className="flex-1 break-words">{t.message}</span>
          <button
            onClick={() => dismiss(t.id)}
            className="opacity-60 hover:opacity-100"
            aria-label="Dismiss"
          >
            <X className="size-4" />
          </button>
        </div>
        );
      })}
    </div>
  );
}
