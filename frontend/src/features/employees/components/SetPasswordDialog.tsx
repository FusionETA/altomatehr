import { useState } from "react";
import { createPortal } from "react-dom";
import { KeyRound, LoaderCircle } from "lucide-react";
import { setEmployeePassword } from "../api";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";

// Give an employee a password the admin chooses.
//
// The self-service reset mails a code to the address on file, which is no use
// to someone who cannot reach that address any more — a returning employee, or
// one whose personal email is gone. This is the way back in.
//
// The typed password is never echoed back or stored anywhere but the hash: the
// admin chose it, so there is nothing here they don't already know, and a
// "show the password you just set" affordance would only put it on screen for
// whoever walks past next.
export function SetPasswordDialog({
  employeeId,
  employeeName,
  onClose,
  onDone,
}: {
  employeeId: string;
  employeeName: string;
  onClose: () => void;
  onDone: () => void;
}) {
  useBodyScrollLock();

  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Typed twice because it is never shown back: a typo would otherwise lock
  // the employee out exactly as thoroughly as the problem this is solving.
  const tooShort = password.length > 0 && password.length < 8;
  const mismatch = confirm.length > 0 && confirm !== password;
  const canSubmit = password.length >= 8 && confirm === password && !busy;

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      await setEmployeePassword(employeeId, password);
      onDone();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not set the password.");
      setBusy(false);
    }
  }

  return createPortal(
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 px-4 py-5 backdrop-blur-md">
      <section className="max-h-[calc(100vh-2.5rem)] w-full max-w-md overflow-y-auto rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)] sm:p-6">
        <h2 className="flex items-center gap-2 text-xl font-black text-foreground">
          <KeyRound className="h-5 w-5 text-primary" />
          Set a new password
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          For <span className="font-semibold text-foreground">{employeeName}</span>. They can sign
          in with it immediately, and change it themselves afterwards.
        </p>

        <label className="mt-4 block">
          <span className="text-xs font-semibold text-muted-foreground">New password</span>
          <input
            type="password"
            autoComplete="new-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className="mt-1 h-11 w-full rounded-xl border border-border/70 bg-background px-3 text-sm text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          />
          {tooShort ? (
            <span className="mt-1 block text-xs font-medium text-destructive">
              At least 8 characters.
            </span>
          ) : null}
        </label>

        <label className="mt-3 block">
          <span className="text-xs font-semibold text-muted-foreground">Type it again</span>
          <input
            type="password"
            autoComplete="new-password"
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
            className="mt-1 h-11 w-full rounded-xl border border-border/70 bg-background px-3 text-sm text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          />
          {mismatch ? (
            <span className="mt-1 block text-xs font-medium text-destructive">
              These don't match.
            </span>
          ) : null}
        </label>

        <p className="mt-3 rounded-2xl border border-border/60 bg-surface-low p-3 text-xs text-muted-foreground">
          Hand it to them over something other than email — the address on file may be exactly what
          they have lost. It is not shown again after you close this.
        </p>

        {error ? (
          <p className="mt-3 text-sm font-medium text-destructive">{error}</p>
        ) : null}

        <div className="mt-5 grid gap-2 sm:grid-cols-2">
          <button
            type="button"
            disabled={!canSubmit}
            onClick={() => void submit()}
            className="flex h-11 items-center justify-center gap-2 rounded-full bg-primary text-sm font-bold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
          >
            {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
            Set password
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={onClose}
            className="h-11 rounded-full border border-border bg-card text-sm font-bold text-foreground transition hover:bg-secondary/50 disabled:opacity-60"
          >
            Cancel
          </button>
        </div>
      </section>
    </div>,
    document.body,
  );
}
