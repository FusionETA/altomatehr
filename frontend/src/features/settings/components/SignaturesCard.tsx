import { useState } from "react";
import { LoaderCircle, PencilLine, PenLine, Plus, RotateCw, Star, Trash2 } from "lucide-react";
import {
  getSignatures,
  removeSignature,
  SIGNATURES_PATH,
  updateSignature,
  type Signature,
} from "../api";
import { MAX_SIGNATURES, signatureLabel } from "../lib/signatures";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { useConfirm } from "@/shared/components/ConfirmDialog";
import { Skeleton } from "@/shared/components/Skeleton";
import { SignatureThumbnail } from "./SignatureThumbnail";
import { AddSignatureDialog } from "./AddSignatureDialog";
import { EditSignatureDialog } from "./EditSignatureDialog";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const PRIMARY_BUTTON =
  "inline-flex h-11 items-center justify-center gap-2 rounded-2xl bg-primary px-5 text-sm font-bold text-primary-foreground shadow-sm transition hover:opacity-90 disabled:opacity-50";
const ROW_BUTTON =
  "inline-flex h-9 items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-xs font-semibold text-foreground transition hover:border-primary hover:text-primary disabled:opacity-50 disabled:hover:border-border disabled:hover:text-foreground";

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

// Saved signatures printed above the signatory's name on letters from
// Documents. Up to 10, one of them the company default.
//
// Lives inside the Organization page's <fieldset>, so for an admin without
// "Change settings" every button here is disabled and the list is read-only —
// the add / edit dialogs can't be opened, which is why their portals sitting
// outside the fieldset doesn't matter.
export function SignaturesCard() {
  const query = useCachedQuery(SIGNATURES_PATH, getSignatures);
  const signatures = query.data ?? [];
  // Only a list that actually loaded says how many there are. After a failed
  // first load `signatures` is an empty stand-in, and treating it as "none
  // yet" would let Add post the "first" signature as default — replacing the
  // real one.
  const loaded = query.data !== undefined;
  const loadFailed = !loaded && !query.loading && query.error !== null;
  const atLimit = signatures.length >= MAX_SIGNATURES;

  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<Signature | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirm, confirmDialog] = useConfirm();

  async function makeDefault(signature: Signature) {
    setBusyId(signature.id);
    setError(null);
    try {
      await updateSignature(signature.id, {
        name: signature.name,
        position: signature.position,
        isDefault: true,
      });
      await query.refresh();
    } catch (err) {
      setError(message(err, "Could not change the default signature."));
    } finally {
      setBusyId(null);
    }
  }

  async function remove(signature: Signature) {
    const ok = await confirm({
      title: `Remove ${signatureLabel(signature)}?`,
      message: signature.isDefault
        ? "New letters can't use it any more; letters already made keep it. It's the company default, so pick another default afterwards."
        : "New letters can't use it any more; letters already made keep it.",
      confirmLabel: "Remove signature",
      destructive: true,
    });
    if (!ok) return;

    setBusyId(signature.id);
    setError(null);
    try {
      await removeSignature(signature.id);
      await query.refresh();
    } catch (err) {
      setError(message(err, "Could not remove the signature."));
    } finally {
      setBusyId(null);
    }
  }

  async function afterSave() {
    setAdding(false);
    setEditing(null);
    await query.refresh();
  }

  return (
    <section className={CARD}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="flex items-center gap-2 text-lg font-black text-foreground">
            <PenLine className="h-5 w-5 text-primary" />
            Signatures
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Printed above the signatory's name on letters from Documents. Up to 10; mark one as the
            company default.
          </p>
        </div>
        <div className="flex flex-col items-end gap-1">
          <button
            type="button"
            onClick={() => setAdding(true)}
            disabled={!loaded || atLimit}
            className={PRIMARY_BUTTON}
          >
            <Plus className="h-4 w-4" />
            Add signature
          </button>
          {atLimit ? (
            <p className="text-xs text-muted-foreground">
              That's the limit of {MAX_SIGNATURES}. Remove one to add another.
            </p>
          ) : null}
        </div>
      </div>

      <div className="mt-4">
        {query.loading && !query.data ? (
          <div className="space-y-2">
            <Skeleton className="h-16 w-full" />
            <Skeleton className="h-16 w-full" />
          </div>
        ) : loadFailed ? (
          <div className="flex flex-col items-center gap-2 rounded-2xl border border-destructive/30 bg-destructive/5 px-4 py-5 text-center">
            <p className="text-sm font-medium text-destructive">
              Couldn't load the signatures. {query.error}
            </p>
            <button
              type="button"
              onClick={() => void query.refresh()}
              className="inline-flex h-9 items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-xs font-semibold text-foreground transition hover:border-primary hover:text-primary"
            >
              <RotateCw className="h-3.5 w-3.5" /> Try again
            </button>
          </div>
        ) : signatures.length === 0 ? (
          <p className="rounded-2xl border border-dashed border-border/70 px-4 py-6 text-center text-sm text-muted-foreground">
            No signatures yet. Letters leave a space to sign by hand.
          </p>
        ) : (
          <ul className="divide-y divide-border/60 overflow-hidden rounded-2xl border border-border/60">
            {signatures.map((s) => (
              <li key={s.id} className="flex flex-wrap items-center gap-3 bg-card px-4 py-3">
                <SignatureThumbnail id={s.id} name={s.name} />
                <div className="min-w-0 flex-1">
                  <p className="flex flex-wrap items-center gap-2 text-sm font-semibold text-foreground">
                    <span className="truncate">{s.name}</span>
                    {s.isDefault ? (
                      <span className="inline-flex items-center gap-1 rounded-full bg-primary/10 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-primary">
                        <Star className="h-3 w-3 fill-current" /> Default
                      </span>
                    ) : null}
                  </p>
                  <p className="truncate text-xs text-muted-foreground">{s.position}</p>
                </div>
                <div className="flex shrink-0 flex-wrap items-center gap-1.5">
                  {busyId === s.id ? (
                    <LoaderCircle className="h-4 w-4 animate-spin text-muted-foreground" />
                  ) : null}
                  {s.isDefault ? null : (
                    <button
                      type="button"
                      onClick={() => void makeDefault(s)}
                      disabled={busyId !== null}
                      className={ROW_BUTTON}
                    >
                      <Star className="h-3.5 w-3.5" /> Set as default
                    </button>
                  )}
                  <button
                    type="button"
                    onClick={() => setEditing(s)}
                    disabled={busyId !== null}
                    aria-label={`Edit ${s.name}`}
                    className={ROW_BUTTON}
                  >
                    <PencilLine className="h-3.5 w-3.5" /> Edit
                  </button>
                  <button
                    type="button"
                    onClick={() => void remove(s)}
                    disabled={busyId !== null}
                    aria-label={`Remove ${s.name}`}
                    className="grid h-9 w-9 place-items-center rounded-xl border border-border bg-card text-destructive transition hover:border-destructive disabled:opacity-50 disabled:hover:border-border"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>

      {error ? <p className="mt-3 text-sm font-medium text-destructive">{error}</p> : null}

      {adding ? (
        <AddSignatureDialog
          isFirst={query.data?.length === 0}
          onClose={() => setAdding(false)}
          onAdded={() => void afterSave()}
        />
      ) : null}
      {editing ? (
        <EditSignatureDialog
          signature={editing}
          onClose={() => setEditing(null)}
          onSaved={() => void afterSave()}
        />
      ) : null}
      {confirmDialog}
    </section>
  );
}
