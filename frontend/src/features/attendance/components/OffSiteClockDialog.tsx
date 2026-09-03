import { useRef, useState } from "react";
import { Camera, LoaderCircle, MapPin, X } from "lucide-react";
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
export function OffSiteClockDialog({
  action,
  distanceMeters,
  busy,
  error,
  onSubmit,
  onClose,
}: Props) {
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

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/35 px-4 py-5 backdrop-blur-sm sm:items-center">
      <section className="max-h-[calc(100vh-2.5rem)] w-full max-w-md overflow-y-auto rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)] sm:p-6">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
              Attendance
            </p>
            <h2 className="mt-1 text-xl font-black text-foreground">{verb} from here</h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="grid h-10 w-10 shrink-0 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground transition hover:text-foreground"
            aria-label={`Cancel clock ${action}`}
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="mt-4 flex items-start gap-2.5 rounded-2xl border border-amber-300/60 bg-amber-50/70 px-3.5 py-3 dark:border-amber-500/30 dark:bg-amber-500/10">
          <MapPin className="mt-0.5 h-4 w-4 shrink-0 text-amber-700 dark:text-amber-400" />
          <p className="text-xs text-amber-900 dark:text-amber-200">
            You&rsquo;re outside the project geofence
            {typeof distanceMeters === "number"
              ? ` — about ${Math.round(distanceMeters)}m away`
              : ""}
            . Add a note and a photo and your supervisor can approve it.
          </p>
        </div>

        <div className="mt-4 grid gap-4">
          <label className="grid gap-1.5">
            <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">
              Why are you off-site?
            </span>
            <textarea
              value={remark}
              onChange={(event) => setRemark(event.target.value)}
              rows={3}
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
                <img src={preview} alt="Selected proof" className="max-h-48 w-full object-cover" />
                <span className="block px-4 py-2 text-xs font-semibold text-muted-foreground">
                  Tap to retake
                </span>
              </button>
            ) : (
              <button
                type="button"
                onClick={() => inputRef.current?.click()}
                className="flex items-center justify-center gap-2 rounded-2xl border border-dashed border-border bg-card px-4 py-6 text-sm font-semibold text-muted-foreground transition hover:text-foreground"
              >
                <Camera className="h-4 w-4" />
                Take or choose a photo
              </button>
            )}
          </div>

          {remark.trim().length === 0 || !file ? (
            <p className="text-xs text-muted-foreground">
              Both a note and a photo are required.
            </p>
          ) : null}

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
      </section>
    </div>
  );
}
