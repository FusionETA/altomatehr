import { useEffect, useRef, useState } from "react";
import { ImageUp, LoaderCircle, PenLine } from "lucide-react";
import { createSignature, type Signature } from "../api";
import {
  SIGNATURE_ACCEPT,
  SIGNATURE_MAX_BYTES,
  signatureFileProblem,
} from "../lib/signatures";
import { strokesToPng, type Stroke } from "../lib/signature-pad";
import { SignaturePad } from "./SignaturePad";
import { SignatureDialogShell, SignatureFields } from "./SignatureDialogParts";

type Tab = "draw" | "upload";

const PRIMARY =
  "inline-flex h-10 items-center gap-2 rounded-2xl bg-primary px-4 text-sm font-semibold text-primary-foreground transition hover:opacity-90 disabled:pointer-events-none disabled:opacity-50";
const SECONDARY =
  "inline-flex h-10 items-center rounded-2xl border border-border bg-card px-4 text-sm font-semibold text-foreground transition hover:bg-muted/60 disabled:opacity-50";

// Add a saved signature: draw it on screen or upload a picture of it, then
// name it. `isFirst` — the server makes the first signature the default on
// its own, so the box shows that rather than offering a choice.
export function AddSignatureDialog({
  isFirst,
  onClose,
  onAdded,
}: {
  isFirst: boolean;
  onClose: () => void;
  onAdded: (signature: Signature) => void;
}) {
  const [tab, setTab] = useState<Tab>("draw");
  const [strokes, setStrokes] = useState<Stroke[]>([]);
  const [file, setFile] = useState<File | null>(null);
  const [fileUrl, setFileUrl] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [position, setPosition] = useState("");
  const [isDefault, setIsDefault] = useState(isFirst);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  // The picked file's preview — revoked when it's replaced or the dialog closes.
  useEffect(() => {
    if (!file) {
      setFileUrl(null);
      return;
    }
    const url = URL.createObjectURL(file);
    setFileUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [file]);

  function pickFile(e: React.ChangeEvent<HTMLInputElement>) {
    const picked = e.target.files?.[0];
    // Cleared so picking the same file again (after fixing it) still fires.
    e.target.value = "";
    if (!picked) return;
    const problem = signatureFileProblem(picked);
    if (problem) {
      setError(problem);
      return;
    }
    setError(null);
    setFile(picked);
  }

  const hasImage = tab === "draw" ? strokes.length > 0 : file !== null;
  const canSave = hasImage && name.trim() !== "" && position.trim() !== "" && !saving;

  async function save() {
    if (!canSave) return;
    setSaving(true);
    setError(null);
    try {
      let image: Blob | null;
      let fileName: string;
      if (tab === "draw") {
        image = await strokesToPng(strokes);
        fileName = "signature.png";
      } else {
        image = file;
        fileName = file?.name ?? "signature";
      }
      if (!image) {
        setError(tab === "draw" ? "Draw your signature first." : "Choose an image first.");
        return;
      }
      if (image.size > SIGNATURE_MAX_BYTES) {
        setError("That signature is over 1 MB. Clear it and draw it again a little smaller.");
        return;
      }
      const added = await createSignature(image, fileName, {
        name: name.trim(),
        position: position.trim(),
        isDefault,
      });
      onAdded(added);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not add the signature.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <SignatureDialogShell
      title="Add signature"
      busy={saving}
      onClose={onClose}
      footer={
        <>
          <button type="button" onClick={onClose} disabled={saving} className={SECONDARY}>
            Cancel
          </button>
          <button type="button" onClick={() => void save()} disabled={!canSave} className={PRIMARY}>
            {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
            Save signature
          </button>
        </>
      }
    >
      <div
        role="tablist"
        aria-label="How to add the signature"
        className="inline-flex rounded-xl border border-border/60 bg-muted/50 p-1 text-sm"
      >
        {(
          [
            { value: "draw", label: "Draw", icon: PenLine },
            { value: "upload", label: "Upload", icon: ImageUp },
          ] as const
        ).map(({ value, label, icon: Icon }) => (
          <button
            key={value}
            type="button"
            role="tab"
            aria-selected={tab === value}
            disabled={saving}
            onClick={() => {
              setTab(value);
              setError(null);
            }}
            className={`inline-flex items-center gap-1.5 rounded-lg px-4 py-1.5 font-semibold transition-colors ${
              tab === value
                ? "bg-primary text-primary-foreground shadow-sm"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            <Icon className="h-3.5 w-3.5" /> {label}
          </button>
        ))}
      </div>

      {tab === "draw" ? (
        <div className="space-y-1.5">
          <SignaturePad strokes={strokes} onChange={setStrokes} disabled={saving} />
          <p className="text-xs text-muted-foreground">
            Use your finger, a stylus or the mouse. Saved as a transparent image cropped to the signature.
          </p>
        </div>
      ) : (
        <div className="space-y-2">
          <input
            ref={fileInput}
            type="file"
            accept={SIGNATURE_ACCEPT}
            className="hidden"
            onChange={pickFile}
          />
          <button
            type="button"
            onClick={() => fileInput.current?.click()}
            disabled={saving}
            className={`flex h-44 w-full items-center justify-center overflow-hidden rounded-2xl border border-dashed border-border p-3 transition hover:border-primary disabled:opacity-60 ${
              // The picked image shows on white, the paper it prints on.
              fileUrl ? "bg-white" : "bg-card"
            }`}
          >
            {fileUrl ? (
              <img src={fileUrl} alt="Chosen signature" className="h-full w-full object-contain" />
            ) : (
              <span className="flex flex-col items-center gap-1.5 text-sm font-semibold text-muted-foreground">
                <ImageUp className="h-5 w-5" />
                Choose an image
                <span className="text-xs font-normal">PNG, JPG or WebP, up to 1 MB</span>
              </span>
            )}
          </button>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="text-xs text-muted-foreground">
              Tip: a dark signature on a white or transparent background prints best.
            </p>
            {file ? (
              <button
                type="button"
                onClick={() => fileInput.current?.click()}
                disabled={saving}
                className="text-xs font-semibold text-primary hover:underline"
              >
                Choose another
              </button>
            ) : null}
          </div>
        </div>
      )}

      <SignatureFields
        name={name}
        position={position}
        isDefault={isDefault}
        disabled={saving}
        defaultLocked={isFirst ? "Your first signature becomes the company default." : null}
        onChange={(next) => {
          if (next.name !== undefined) setName(next.name);
          if (next.position !== undefined) setPosition(next.position);
          if (next.isDefault !== undefined) setIsDefault(next.isDefault);
        }}
      />

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}
    </SignatureDialogShell>
  );
}
