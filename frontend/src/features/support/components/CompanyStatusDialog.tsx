import { useState } from "react";
import { createPortal } from "react-dom";
import { LoaderCircle, X } from "lucide-react";
import { deactivateOrganization, reactivateOrganization, type SupportOrganization } from "../api";

const REASON_MAX = 500;

// Deactivate (with a reason) or reactivate a company. Deactivating closes it
// to everyone in it at once — their next click signs them out — and stops its
// reminders and automatic jobs; nothing is deleted, and reactivating brings it
// all back. The reason stays on this page; the company never sees it.
export function CompanyStatusDialog({
  org,
  onClose,
  onSaved,
}: {
  org: SupportOrganization;
  onClose: () => void;
  onSaved: () => void;
}) {
  const deactivating = org.deactivatedAt === null;
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const reasonOk = reason.trim().length >= 3;

  async function save() {
    setSaving(true);
    setError(null);
    try {
      if (deactivating) await deactivateOrganization(org.id, reason.trim());
      else await reactivateOrganization(org.id);
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : "That did not go through.");
      setSaving(false);
    }
  }

  return createPortal(
    <div
      className="fixed inset-0 z-[70] flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm"
      onClick={onClose}
    >
      <div
        className="w-full max-w-md rounded-[28px] border border-border/70 bg-card p-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <h3 className="text-base font-black text-foreground">
              {deactivating ? "Deactivate company" : "Reactivate company"}
            </h3>
            <p className="truncate text-sm text-muted-foreground">{org.name}</p>
          </div>
          <button
            type="button"
            aria-label="Close"
            onClick={onClose}
            className="rounded-full p-2 text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="mt-5 space-y-4">
          {deactivating ? (
            <>
              <ul className="list-disc space-y-1 pl-5 text-sm text-muted-foreground">
                <li>Nobody can sign in to it, and anyone signed in is signed out on their next click.</li>
                <li>Its API keys stop working.</li>
                <li>Reminders, auto clock-out and other automatic jobs skip it. Leave balances keep accruing.</li>
                <li>Nothing is deleted. Reactivate it any time and everything works again.</li>
              </ul>
              <label className="block space-y-1.5">
                <span className="text-sm font-semibold text-foreground">Reason</span>
                <textarea
                  value={reason}
                  onChange={(e) => setReason(e.target.value.slice(0, REASON_MAX))}
                  rows={3}
                  placeholder="e.g. Subscription unpaid since September"
                  className="w-full resize-none rounded-2xl border border-border bg-background px-3 py-2 text-sm text-foreground outline-none transition focus:border-primary"
                />
                <span className="block text-xs text-muted-foreground">
                  Only Fusioneta sees this; the company doesn't.
                </span>
              </label>
            </>
          ) : (
            <div className="space-y-2 text-sm text-muted-foreground">
              <p>Everyone in it can sign in again, and its automatic jobs pick up where they stopped.</p>
              {org.deactivatedReason ? (
                <p>
                  Deactivated for: <span className="text-foreground">{org.deactivatedReason}</span>
                </p>
              ) : null}
            </div>
          )}

          {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

          <div className="flex justify-end gap-2">
            <button
              type="button"
              onClick={onClose}
              disabled={saving}
              className="rounded-2xl bg-muted px-4 py-2 text-sm font-semibold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
            >
              Cancel
            </button>
            <button
              type="button"
              onClick={() => void save()}
              disabled={saving || (deactivating && !reasonOk)}
              className={`inline-flex items-center gap-2 rounded-2xl px-4 py-2 text-sm font-semibold transition disabled:opacity-50 ${
                deactivating
                  ? "bg-destructive text-destructive-foreground hover:bg-destructive/90"
                  : "bg-primary text-primary-foreground hover:bg-primary/90"
              }`}
            >
              {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
              {deactivating ? "Deactivate" : "Reactivate"}
            </button>
          </div>
        </div>
      </div>
    </div>,
    document.body,
  );
}
