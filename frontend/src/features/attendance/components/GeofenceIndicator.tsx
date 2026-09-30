import { useEffect, useState } from "react";
import { AlertTriangle, LoaderCircle, RefreshCw } from "lucide-react";
import { requestGeolocation } from "@/shared/lib/geolocation";
import { checkGeofence, type GeofenceCheck } from "../api";

// The clock card's live line, as in the previous system:
//   ● On site · 45 m away
//   ● Off site · 612 m away (limit 500 m)
// so an employee knows BEFORE tapping whether the clock will ask them for a
// reason and a photo. The verdict comes from the server's own geofence check —
// the organization's radius, every site of the project, the employee's policy —
// never from arithmetic here, so it cannot disagree with the clock itself.
export function GeofenceIndicator({ projectId }: { projectId: string | undefined }) {
  const [state, setState] = useState<"locating" | "denied" | "error" | "ok">("locating");
  const [check, setCheck] = useState<GeofenceCheck | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setState("locating");
    (async () => {
      let coords;
      try {
        coords = await requestGeolocation();
      } catch {
        if (!cancelled) setState("denied");
        return;
      }
      try {
        const result = await checkGeofence(projectId, coords.lat, coords.lng);
        if (cancelled) return;
        setCheck(result);
        setState("ok");
      } catch {
        if (!cancelled) setState("error");
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [projectId, attempt]);

  const retry = (
    <button
      type="button"
      onClick={() => setAttempt((n) => n + 1)}
      className="ml-1 inline-flex items-center text-muted-foreground hover:text-foreground"
      aria-label="Check my location again"
    >
      <RefreshCw className="h-3 w-3" />
    </button>
  );

  if (state === "locating") {
    return (
      <p className="inline-flex items-center gap-1.5 text-[11px] font-semibold text-muted-foreground">
        <LoaderCircle className="h-3 w-3 animate-spin" />
        Locating you…
      </p>
    );
  }
  if (state === "denied" || state === "error") {
    return (
      <p className="inline-flex items-center gap-1.5 text-[11px] font-semibold text-amber-700 dark:text-amber-400">
        <AlertTriangle className="h-3 w-3" />
        {state === "denied" ? "Location unavailable" : "Couldn't check your location"}
        {retry}
      </p>
    );
  }
  if (!check || !check.geofenced || check.distanceMeters == null) {
    return <p className="text-[11px] font-semibold text-muted-foreground">No geofence set</p>;
  }

  const away = `${formatDistance(check.distanceMeters)} away`;
  if (check.withinRadius) {
    return (
      <p className="inline-flex items-center gap-1.5 text-[11px] font-semibold text-success">
        <span className="h-1.5 w-1.5 rounded-full bg-success" />
        On site · {away}
        {retry}
      </p>
    );
  }
  return (
    <p className="inline-flex items-center gap-1.5 text-[11px] font-semibold text-amber-700 dark:text-amber-400">
      <span className="h-1.5 w-1.5 rounded-full bg-amber-500" />
      Off site · {away} (limit {check.radiusMeters} m)
      {/* Outside, but the policy doesn't enforce the geofence: nothing will be
          asked, so the line only states where they are. */}
      {check.enforced ? null : <span className="font-normal text-muted-foreground"> · not required</span>}
      {retry}
    </p>
  );
}

function formatDistance(metres: number) {
  return metres >= 1000 ? `${(metres / 1000).toFixed(1)} km` : `${Math.round(metres)} m`;
}
