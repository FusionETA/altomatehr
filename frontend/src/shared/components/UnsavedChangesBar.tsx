import { LoaderCircle, RotateCcw } from "lucide-react";

// The save bar pinned to the bottom of a long form, shown only while there is
// something to save. "Did that save?" is never a question — the bar is either
// there (unsaved) or gone (saved) — and an untouched record carries no call to
// action at all.
//
// Shared so every editable page saves the same way. A page renders it
// conditionally and gives its content bottom padding (pb-24) so the last
// field is never hidden underneath.
export function UnsavedChangesBar({
  message = "Unsaved changes",
  saving,
  error,
  onSave,
  onDiscard,
  saveLabel = "Save changes",
}: {
  message?: string;
  saving: boolean;
  // A refused save stays on the bar, next to the button that caused it.
  error?: string | null;
  onSave: () => void;
  // Omitted when there is nothing to go back to — e.g. defaults that have
  // never been saved, where the only action is to save them.
  onDiscard?: () => void;
  saveLabel?: string;
}) {
  return (
    <div className="fixed inset-x-0 bottom-0 z-40 border-t border-border/70 bg-card/95 px-4 py-3 backdrop-blur-xl">
      <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-3">
        <div className="min-w-0">
          <p className="text-sm font-semibold text-foreground">{message}</p>
          {error ? (
            <p role="alert" className="text-xs font-medium text-destructive">
              {error}
            </p>
          ) : null}
        </div>
        <div className="flex items-center gap-2">
          {onDiscard ? (
            <button
              type="button"
              onClick={onDiscard}
              disabled={saving}
              className="inline-flex h-11 items-center gap-1.5 rounded-full border border-border bg-card px-4 text-sm font-bold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
            >
              <RotateCcw className="h-3.5 w-3.5" />
              Discard
            </button>
          ) : null}
          <button
            type="button"
            onClick={onSave}
            disabled={saving}
            className="inline-flex h-11 items-center gap-2 rounded-full bg-primary px-5 text-sm font-bold text-primary-foreground shadow-sm transition hover:opacity-90 disabled:opacity-60"
          >
            {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
            {saveLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
