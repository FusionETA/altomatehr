// Thin promise wrapper around the browser Geolocation API. Works on localhost
// and HTTPS (a "secure context"); rejects with a friendly message otherwise.
export type Coords = { lat: number; lng: number };

export function requestGeolocation(timeoutMs = 10_000): Promise<Coords> {
  return new Promise((resolve, reject) => {
    if (!("geolocation" in navigator)) {
      reject(new Error("Location isn't available in this browser."));
      return;
    }
    navigator.geolocation.getCurrentPosition(
      (pos) => resolve({ lat: pos.coords.latitude, lng: pos.coords.longitude }),
      (err) =>
        reject(
          new Error(
            err.code === err.PERMISSION_DENIED
              ? "Location permission denied."
              : "Couldn't get your location.",
          ),
        ),
      { enableHighAccuracy: true, timeout: timeoutMs, maximumAge: 0 },
    );
  });
}

// Human-readable distance — metres under 1km, km above (ported from lib/geo.ts).
export function formatDistance(meters: number): string {
  return meters < 1000 ? `${Math.round(meters)}m` : `${(meters / 1000).toFixed(2)} km`;
}
