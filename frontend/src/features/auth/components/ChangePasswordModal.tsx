import { useState, type FormEvent } from "react";
import { createPortal } from "react-dom";
import { KeyRound, LoaderCircle, X } from "lucide-react";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";
import { changePassword, MIN_PASSWORD_LENGTH } from "../api";

// Change password for a signed-in user.
//
// On success every session is revoked server-side, this one included, so there
// is no "saved" state to return to — the only honest ending is to sign the user
// out. The modal says so before they commit, rather than dropping them at the
// login screen unexplained.
export function ChangePasswordModal({
  onClose,
  onChanged,
}: {
  onClose: () => void;
  /** Called after a successful change: sign the user out. */
  onChanged: () => void;
}) {
  useBodyScrollLock();

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (newPassword !== confirm) {
      setError("The two new passwords don't match.");
      return;
    }
    if (newPassword.length < MIN_PASSWORD_LENGTH) {
      setError(`Use at least ${MIN_PASSWORD_LENGTH} characters.`);
      return;
    }
    if (newPassword === currentPassword) {
      setError("Choose a password you haven't used here before.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await changePassword({ currentPassword, newPassword });
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not change the password.");
      setBusy(false);
    }
  }

  // Portaled: the shell header sets backdrop-filter, and any ancestor with a
  // filter becomes the containing block for position:fixed — the overlay would
  // cover the header instead of the screen.
  return createPortal(
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 px-4 py-5 backdrop-blur-md">
      <section className="max-h-[calc(100vh-2.5rem)] w-full max-w-md overflow-y-auto rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)] sm:p-6">
        <div className="flex items-start justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="grid h-11 w-11 place-items-center rounded-2xl bg-primary/10 text-primary">
              <KeyRound className="h-5 w-5" />
            </div>
            <div>
              <h2 className="text-xl font-black text-foreground">Change password</h2>
              <p className="text-xs text-muted-foreground">You'll be signed out afterwards.</p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="grid h-10 w-10 shrink-0 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground transition hover:text-foreground"
            aria-label="Close"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <form onSubmit={submit} className="mt-5 space-y-4">
          <label className="block space-y-2">
            <span className="text-sm font-semibold">Current password</span>
            <input
              required
              autoFocus
              type="password"
              autoComplete="current-password"
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
              className={INPUT}
            />
          </label>

          <label className="block space-y-2">
            <span className="text-sm font-semibold">New password</span>
            <input
              required
              type="password"
              autoComplete="new-password"
              minLength={MIN_PASSWORD_LENGTH}
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              className={INPUT}
            />
            <span className="block text-xs text-muted-foreground">
              At least {MIN_PASSWORD_LENGTH} characters.
            </span>
          </label>

          <label className="block space-y-2">
            <span className="text-sm font-semibold">Confirm new password</span>
            <input
              required
              type="password"
              autoComplete="new-password"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              className={INPUT}
            />
          </label>

          <p className="rounded-2xl border border-border/60 bg-surface-low px-4 py-3 text-xs text-muted-foreground">
            Changing your password signs out every device, including this one. Anyone using a
            copied session loses access straight away.
          </p>

          {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

          <div className="grid gap-2 sm:grid-cols-2">
            <button
              type="submit"
              disabled={busy}
              className="flex h-11 items-center justify-center gap-2 rounded-full bg-primary text-sm font-bold text-primary-foreground transition hover:opacity-90 disabled:opacity-60"
            >
              {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
              Change password
            </button>
            <button
              type="button"
              onClick={onClose}
              className="h-11 rounded-full border border-border bg-card text-sm font-bold text-foreground transition hover:bg-secondary/50"
            >
              Cancel
            </button>
          </div>
        </form>
      </section>
    </div>,
    document.body,
  );
}

const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-background px-4 text-sm text-foreground shadow-sm outline-none transition focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2";
