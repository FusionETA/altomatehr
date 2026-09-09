import { useEffect, useRef } from "react";
import { subscribeRealtime, type RealtimeEvent, type RealtimeScope } from "./realtime-client";

// Calls `onEvent` whenever a realtime nudge for one of `scopes` arrives —
// e.g. an admin's claims table re-fetching itself the moment a claim is
// submitted elsewhere, instead of waiting for the next navigation.
export function useRealtimeEvent(scopes: RealtimeScope[], onEvent: (event: RealtimeEvent) => void) {
  // Refs so a new inline `onEvent`/`scopes` each render doesn't churn the
  // subscription — only mount/unmount should (re)subscribe.
  const onEventRef = useRef(onEvent);
  onEventRef.current = onEvent;
  const scopesRef = useRef(scopes);
  scopesRef.current = scopes;

  useEffect(() => {
    return subscribeRealtime((event) => {
      if (scopesRef.current.includes(event.scope)) onEventRef.current(event);
    });
  }, []);
}
