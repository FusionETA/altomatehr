import { useState } from "react";
import { LoaderCircle } from "lucide-react";
import { updateSignature, type Signature } from "../api";
import { SignatureThumbnail } from "./SignatureThumbnail";
import { SignatureDialogShell, SignatureFields } from "./SignatureDialogParts";

// Change the name / position printed under a saved signature, or make it the
// company default. The image itself can't be swapped: add a new signature and
// remove this one instead, so letters already made keep pointing at theirs.
export function EditSignatureDialog({
  signature,
  onClose,
  onSaved,
}: {
  signature: Signature;
  onClose: () => void;
  onSaved: (signature: Signature) => void;
}) {
  const [name, setName] = useState(signature.name);
  const [position, setPosition] = useState(signature.position);
  const [isDefault, setIsDefault] = useState(signature.isDefault);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const canSave = name.trim() !== "" && position.trim() !== "" && !saving;

  async function save() {
    if (!canSave) return;
    setSaving(true);
    setError(null);
    try {
      onSaved(
        await updateSignature(signature.id, {
          name: name.trim(),
          position: position.trim(),
          isDefault,
        }),
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save the signature.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <SignatureDialogShell
      title="Edit signature"
      busy={saving}
      onClose={onClose}
      footer={
        <>
          <button
            type="button"
            onClick={onClose}
            disabled={saving}
            className="inline-flex h-10 items-center rounded-2xl border border-border bg-card px-4 text-sm font-semibold text-foreground transition hover:bg-muted/60 disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={() => void save()}
            disabled={!canSave}
            className="inline-flex h-10 items-center gap-2 rounded-2xl bg-primary px-4 text-sm font-semibold text-primary-foreground transition hover:opacity-90 disabled:pointer-events-none disabled:opacity-50"
          >
            {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
            Save changes
          </button>
        </>
      }
    >
      <SignatureThumbnail id={signature.id} name={signature.name} className="h-28 w-full" />
      <SignatureFields
        name={name}
        position={position}
        isDefault={isDefault}
        disabled={saving}
        defaultLocked={
          signature.isDefault
            ? "This is the company default. To change that, make another signature the default."
            : null
        }
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
