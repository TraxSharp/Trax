// Browser-notification preference. Off by default; the user opts in from User settings,
// which triggers the permission prompt. We store the intent separately from the browser's
// permission grant so a revoked grant surfaces as "needs re-enabling" rather than silently on.
const KEY = "trax:notify-failures";

export function notifyEnabled(): boolean {
  return localStorage.getItem(KEY) === "1";
}

export function setNotifyEnabled(on: boolean) {
  localStorage.setItem(KEY, on ? "1" : "0");
}

export function notificationsSupported(): boolean {
  return typeof window !== "undefined" && "Notification" in window;
}

// Returns the resulting permission ("granted" | "denied" | "default" | "unsupported").
export async function requestNotifyPermission(): Promise<string> {
  if (!notificationsSupported()) return "unsupported";
  if (Notification.permission === "granted") return "granted";
  return await Notification.requestPermission();
}

export function fireFailureNotification(trainName: string, reason: string | null) {
  if (!notifyEnabled() || !notificationsSupported()) return;
  if (Notification.permission !== "granted") return;
  const short = trainName.split(".").pop() ?? trainName;
  new Notification("Train failed", {
    body: reason ? `${short}: ${reason}` : short,
    tag: `trax-fail-${trainName}`,
  });
}

// How many failures may still raise a toast (and a browser notification) in this page load. Unlimited
// unless set: the demo build allows two, since its replayed feed fails the same runs on every loop.
let failureAlertsLeft = Infinity;

/** Limits the failures that raise an alert for the rest of this page load; later ones stay silent. */
export function setFailureAlertLimit(max: number) {
  failureAlertsLeft = max;
}

/** Spends one failure alert; false when none are left. */
export function takeFailureAlert(): boolean {
  if (failureAlertsLeft <= 0) return false;
  failureAlertsLeft--;
  return true;
}
