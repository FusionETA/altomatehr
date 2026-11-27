import { useState, type FormEvent } from "react";
import { createPortal } from "react-dom";
import { KeyRound, LoaderCircle, X } from "lucide-react";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";
import {
  changeMyPayslipPassword,
  PAYSLIP_PASSWORD_MAX_LENGTH,
  PAYSLIP_PASSWORD_MIN_LENGTH,
} from "../api";

// The employee chooses their own payslip password — the one that opens every
// payslip PDF of theirs. Not their sign-in password: this one travels with
// every payslip they send on, so the server refuses that one.
//
// Unlike changing the sign-in password, nobody is signed out: it only changes
// how the files are locked from now on.
export function ChangePayslipPasswordModal({
  onClose,
  onChanged,
}: {
  onClose: () => void;
  onChanged: () => void;
}) {
  useBodyScrollLock();

  const [newPassword, setNewPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (newPassword !== confirm) {
      setError("The two passwords don't match.");
      return;
    }
    if (newPassword.length < PAYSLIP_PASSWORD_MIN_LENGTH) {
      setError(`Use at least ${PAYSLIP_PASSWORD_MIN_LENGTH} characters.`);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await changeMyPayslipPassword(newPassword);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not change your payslip password.");
      setBusy(false);
    }
  }

  // Portaled: the shell header sets backdrop-filter, and any ancestor with a
  // filter becomes the containing block for position:fixed.
  return createPortal(
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 px-4 py-5 backdrop-blur-md">
      <section className="max-h-[calc(100vh-2.5rem)] w-full max-w-md overflow-y-auto rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)] sm:p-6">
        <div className="flex items-start justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="grid h-11 w-11 place-items-center rounded-2xl bg-primary/10 text-primary">
              <KeyRound className="h-5 w-5" />
            </div>
            <div>
              <h2 className="text-xl font-black text-foreground">Change payslip password</h2>
              <p className="text-xs text-muted-foreground">Opens every payslip PDF you download.</p>
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
            <span className="text-sm font-semibold">New payslip password</span>
            <input
              required
              autoFocus
              type="password"
              autoComplete="new-password"
              minLength={PAYSLIP_PASSWORD_MIN_LENGTH}
              maxLength={PAYSLIP_PASSWORD_MAX_LENGTH}
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              className={INPUT}
            />
            <span className="block text-xs text-muted-foreground">
              At least {PAYSLIP_PASSWORD_MIN_LENGTH} characters, and not the password you sign in with.
            </span>
          </label>

          <label className="block space-y-2">
            <span className="text-sm font-semibold">Confirm new payslip password</span>
            <input
              required
              type="password"
              autoComplete="new-password"
              maxLength={PAYSLIP_PASSWORD_MAX_LENGTH}
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              className={INPUT}
            />
          </label>

          <p className="rounded-2xl border border-border/60 bg-surface-low px-4 py-3 text-xs text-muted-foreground">
            Payslips you download from now on open with the new password. Copies you already
            downloaded or were emailed still open with the old one.
          </p>

          {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

          <div className="grid gap-2 sm:grid-cols-2">
            <button
              type="submit"
              disabled={busy}
              className="flex h-11 items-center justify-center gap-2 rounded-full bg-primary text-sm font-bold text-primary-foreground transition hover:opacity-90 disabled:opacity-60"
            >
              {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
              Save password
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
