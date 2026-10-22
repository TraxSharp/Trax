import { useEffect, useRef } from "react";
import { fireFailureNotification, takeFailureAlert } from "../lib/notify";
import { toast } from "../lib/toast";
import { useTraxSubscription } from "../lib/consoleClient";
import { onTrainStateChangedDocument } from "../trax/index.g";

// Mounted once under the Layout. Watches the live state feed and surfaces failures as a
// toast plus (if the user opted in) a browser notification. Dedupes by metadataId so a
// re-delivered event doesn't double-fire.
export function NotificationBridge() {
  const seen = useRef<Set<number>>(new Set());

  const [{ data: frame }] = useTraxSubscription(onTrainStateChangedDocument);
  const data = frame?.onTrainStateChanged;

  useEffect(() => {
    if (!data || data.trainState !== "FAILED") return;
    if (seen.current.has(data.metadataId)) return;
    seen.current.add(data.metadataId);
    if (!takeFailureAlert()) return;

    const short = data.trainName.split(".").pop() ?? data.trainName;
    toast(
      data.failureReason ? `${short} failed: ${data.failureReason}` : `${short} failed`,
      "error",
    );
    fireFailureNotification(data.trainName, data.failureReason);
  }, [data]);

  return null;
}
