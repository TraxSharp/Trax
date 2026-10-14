import { useEffect, useRef } from "react";
import { useSubscription } from "urql";
import { ON_TRAIN_STATE_CHANGED } from "../graphql/subscriptions";
import { fireFailureNotification, takeFailureAlert } from "../lib/notify";
import { toast } from "../lib/toast";
import type { TrainLifecycleEvent } from "../types";

// Mounted once under the Layout. Watches the live state feed and surfaces failures as a
// toast plus (if the user opted in) a browser notification. Dedupes by metadataId so a
// re-delivered event doesn't double-fire.
export function NotificationBridge() {
  const seen = useRef<Set<number>>(new Set());

  const [{ data }] = useSubscription<
    { onTrainStateChanged: TrainLifecycleEvent },
    TrainLifecycleEvent | null
  >({ query: ON_TRAIN_STATE_CHANGED }, (_prev, d) => d.onTrainStateChanged);

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
