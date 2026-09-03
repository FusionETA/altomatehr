import { useRef, useState } from "react";
import { Camera, LoaderCircle, MapPin, X } from "lucide-react";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";
import { uploadAttendancePhoto } from "../api";

export type OffSiteProof = { remark: string; photoUrl: string };

type Props = {
  /** "in" or "out" — only changes the wording. */
  action: "in" | "out";
  /** Metres from the project, when the server reported it. */
  distanceMeters?: number | null;
  busy: boolean;
  error: string | null;
  onSubmit: (proof: OffSiteProof) => void;
  onClose: () => void;
};

// Collects the remark and photo the server requires to clock from outside the
// project geofence, then hands them back so the caller can retry.
//
// This replaces sending the person to the Attendance screen "to finish it
// there" — that screen has no clock form, so the hand-off was a dead end and
// the tap just appeared to do nothing.
//
// Both fields are mandatory because the server treats either one missing as no
// proof at all (OffSiteProofMissing), so letting it submit with one would only
// produce the same refusal a second time.
// 11562m reads as noise; 11.6 km reads as "you are nowhere near the site".
function formatDistance(meters: number) {
  return meters >= 1000
    ? `${(meters / 1000).toFixed(1)} km away`
    : `${Math.round(meters)}m away`;
}

export function OffSiteClockDialog({
  action,
  distanceMeters,
  busy,
  error,
  onSubmit,
  onClose,
}: Props) {
  useBodyScrollLock();

  const [remark, setRemark] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const verb = action === "in" ? "Clock in" : "Clock out";
  const canSubmit = remark.trim().length > 0 && file !== null && !busy && !uploading;

  function pick(chosen: File | null) {
    setUploadError(null);
    setFile(chosen);
    setPreview((old) => {
      if (old) URL.revokeObjectURL(old);
      return chosen ? URL.createObjectURL(chosen) : null;
    });
  }

  async function submit() {
    if (!file) return;
    setUploading(true);
    setUploadError(null);
    try {
      // Upload first: the clock call takes a URL, not the bytes.
      const { photoUrl } = await uploadAttendancePhoto(file);
      onSubmit({ remark: remark.trim(), photoUrl });
    } catch (e: unknown) {
      setUploadError(e instanceof Error ? e.message : "Could not upload that photo.");
    } finally {
      setUploading(false);
    }
  }

  // No onClick on the backdrop, deliberately: a stray tap while typing a
  // remark or picking a photo would discard the whole thing. Closing is the
  // X or Cancel, both explicit.
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/50 p-3 backdrop-blur-md sm:items-center sm:p-4">
      <section className="flex max-h-[calc(100dvh-1.5rem)] w-full max-w-md flex-col overflow-hidden rounded-[26px] border border-border/70 bg-card shadow-[0_24px_70px_rgba(32,10,55,0.24)]">
        <header className="flex shrink-0 items-center justify-between gap-3 border-b border-border/60 px-5 py-3.5">
          <h2 className="text-base font-black text-foreground">{verb} from here</h2>
          <button
            type="button"
            onClick={onClose}
            className="grid h-8 w-8 shrink-0 place-items-center rounded-full text-muted-foreground transition hover:bg-secondary/60 hover:text-foreground"
            aria-label={`Cancel clock ${action}`}
          >
            <X className="h-4 w-4" />
          </button>
        </header>

        <div className="min-h-0 flex-1 overflow-y-auto px-5 pb-5 pt-4">
          <div className="flex items-start gap-2.5 rounded-2xl bg-amber-50 px-3.5 py-2.5 dark:bg-amber-500/10">
            <MapPin className="mt-0.5 h-4 w-4 shrink-0 text-amber-600 dark:text-amber-400" />
            <p className="text-xs leading-relaxed text-amber-900 dark:text-amber-200">
              {typeof distanceMeters === "number" ? (
                <>
                  You&rsquo;re <b className="font-bold">{formatDistance(distanceMeters)}</b> from the
                  project site.
                </>
              ) : (
                <>You&rsquo;re outside the project geofence.</>
              )}{" "}
              Add a note and a photo so your supervisor can approve it.
            </p>
          </div>

          <div className="mt-4 grid gap-3.5">
            <label className="grid gap-1.5">
              <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">
                Why are you off-site?
              </span>
              <textarea
                value={remark}
                onChange={(event) => setRemark(event.target.value)}
                rows={2}
                placeholder="Client visit, site inspection, working from home..."
                className="resize-none rounded-2xl border border-border bg-card px-4 py-3 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              />
            </label>

            <div className="grid gap-1.5">
              <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">
                Photo
              </span>
              {/* capture="environment" opens the camera straight away on a phone,
                  but still allows picking a file on desktop. */}
              <input
                ref={inputRef}
                type="file"
                accept="image/*"
                capture="environment"
                className="hidden"
                onChange={(event) => pick(event.target.files?.[0] ?? null)}
              />
              {preview ? (
                <button
                  type="button"
                  onClick={() => inputRef.current?.click()}
                  className="overflow-hidden rounded-2xl border border-border bg-card"
                >
                  <img src={preview} alt="Selected proof" className="max-h-36 w-full object-cover" />
                  <span className="block px-4 py-2 text-xs font-semibold text-muted-foreground">
                    Tap to retake
                  </span>
                </button>
              ) : (
                <button
                  type="button"
                  onClick={() => inputRef.current?.click()}
                  className="flex items-center justify-center gap-2 rounded-2xl border border-dashed border-border bg-card px-4 py-5 text-sm font-semibold text-muted-foreground transition hover:text-foreground"
                >
                  <Camera className="h-4 w-4" />
                  Take or choose a photo
                </button>
              )}
            </div>

            <div className="grid gap-2 sm:grid-cols-2">
              <button
                type="button"
                onClick={() => void submit()}
                disabled={!canSubmit}
                className="flex h-11 items-center justify-center gap-2 rounded-full bg-primary text-sm font-bold text-primary-foreground transition hover:opacity-90 disabled:opacity-60"
              >
                {busy || uploading ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                {verb}
              </button>
              <button
                type="button"
                onClick={onClose}
                className="h-11 rounded-full border border-border bg-card text-sm font-bold text-foreground transition hover:bg-secondary/50"
              >
                Cancel
              </button>
            </div>

            {uploadError ?? error ? (
              <p className="text-sm font-medium text-destructive">{uploadError ?? error}</p>
            ) : null}
          </div>
        </div>
      </section>
    </div>
  );
}
