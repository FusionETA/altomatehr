import { useState } from "react";
import { FileImage } from "lucide-react";

import { openAttendancePhoto } from "../api";

/**
 * A clock-in/out selfie, as a compact control beside the time it belongs to.
 *
 * A button rather than an inline `<img>` thumbnail: the photo endpoint is
 * bearer-authenticated, so a plain `src` cannot load it — `openAttendancePhoto`
 * fetches the blob with the token attached. Rendering real thumbnails across a
 * board of rows would mean one authenticated image fetch per row on every
 * load, so the picture stays one click away.
 *
 * Lives in the attendance feature rather than beside the admin table because
 * both the admin screens and the shared session list need it.
 */
export function AttendancePhotoButton({ url, label }: { url: string | null; label: string }) {
  // A record can point at a photo the storage no longer holds — the pruning
  // job deletes files, and the Performance tab counts the leftovers as
  // "missing files". Opening one of those 404s, so the failure is shown on the
  // control instead of vanishing into an unhandled rejection.
  const [failed, setFailed] = useState(false);

  if (!url) return null;

  const title = failed ? `${label} — the file is no longer in storage` : label;

  return (
    <button
      type="button"
      onClick={() => {
        setFailed(false);
        openAttendancePhoto(url).catch(() => setFailed(true));
      }}
      aria-label={title}
      title={title}
      className={`inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full border transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring ${
        failed
          ? "border-destructive/40 text-destructive"
          : "border-border/70 text-muted-foreground hover:bg-surface-low hover:text-primary"
      }`}
    >
      <FileImage className="h-3 w-3" aria-hidden />
    </button>
  );
}
