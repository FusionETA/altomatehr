import { useState, type FormEvent } from "react";
import { ArrowLeft, CircleCheck, LoaderCircle, MailCheck } from "lucide-react";
import { forgotPassword, MIN_PASSWORD_LENGTH, resetPassword } from "../api";

// Two-step password reset: ask for the code, then redeem it.
//
// The server deliberately answers /forgot-password with 204 whether or not the
// address has an account, so this cannot say "code sent" — that would leak the
// very thing the endpoint refuses to. It says "if that address has an account"
// and moves on regardless, which is also why step 2 is reachable for an email
// that was never registered: it has to be, or the wording would be a lie.
export function ForgotPasswordForm({
  initialEmail,
  onBackToLogin,
}: {
  initialEmail?: string;
  onBackToLogin: () => void;
}) {
  const [step, setStep] = useState<"request" | "redeem" | "done">("request");
  const [email, setEmail] = useState(initialEmail ?? "");
  const [otp, setOtp] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function requestCode(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await forgotPassword(email.trim());
      setStep("redeem");
    } catch (err) {
      // Only a transport or rate-limit failure can land here — the endpoint
      // itself doesn't fail on an unknown address.
      setError(err instanceof Error ? err.message : "Could not send the code.");
    } finally {
      setBusy(false);
    }
  }

  async function redeem(e: FormEvent) {
    e.preventDefault();
    if (newPassword !== confirm) {
      setError("The two passwords don't match.");
      return;
    }
    if (newPassword.length < MIN_PASSWORD_LENGTH) {
      setError(`Use at least ${MIN_PASSWORD_LENGTH} characters.`);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await resetPassword({ email: email.trim(), otp: otp.trim(), newPassword });
      setStep("done");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not reset the password.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="flex min-h-[100svh] items-center px-4 py-4 text-foreground sm:min-h-screen sm:px-6 sm:py-10 lg:px-8">
      <div className="mx-auto w-full max-w-xl">
        <section className="rounded-[32px] border border-border/60 bg-card/80 px-5 py-5 shadow-panel backdrop-blur-xl sm:px-8 sm:py-8">
          <button
            type="button"
            onClick={onBackToLogin}
            className="inline-flex items-center gap-1.5 text-sm font-semibold text-muted-foreground transition hover:text-foreground"
          >
            <ArrowLeft className="h-4 w-4" />
            Back to login
          </button>

          {step === "done" ? (
            <div className="mt-6 text-center">
              <div className="mx-auto grid h-14 w-14 place-items-center rounded-2xl bg-success/10 text-success">
                <CircleCheck className="h-7 w-7" />
              </div>
              <h1 className="mt-4 text-2xl font-black">Password changed</h1>
              <p className="mt-1 text-sm text-muted-foreground">
                Sign in with your new password.
              </p>
              <button
                type="button"
                onClick={onBackToLogin}
                className="mt-6 inline-flex h-12 w-full items-center justify-center rounded-2xl bg-primary px-4 text-sm font-semibold text-primary-foreground shadow-panel transition hover:bg-primary/90"
              >
                Back to login
              </button>
            </div>
          ) : step === "request" ? (
            <form onSubmit={requestCode} className="mt-6 space-y-5">
              <div>
                <h1 className="text-2xl font-black">Forgot your password?</h1>
                <p className="mt-1 text-sm text-muted-foreground">
                  Enter your work email and we'll send a 6-digit code.
                </p>
              </div>

              <label className="block space-y-2">
                <span className="text-sm font-semibold">Email</span>
                <input
                  required
                  type="email"
                  autoComplete="username"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="you@company.com"
                  className={INPUT}
                />
              </label>

              {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

              <button type="submit" disabled={busy} className={PRIMARY}>
                {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                Send code
              </button>
            </form>
          ) : (
            <form onSubmit={redeem} className="mt-6 space-y-5">
              <div>
                <div className="grid h-12 w-12 place-items-center rounded-2xl bg-primary/10 text-primary">
                  <MailCheck className="h-5 w-5" />
                </div>
                <h1 className="mt-4 text-2xl font-black">Check your email</h1>
                {/* Not "we sent a code" — see the note at the top of this file. */}
                <p className="mt-1 text-sm text-muted-foreground">
                  If <span className="font-semibold text-foreground">{email.trim()}</span> has an
                  account, a 6-digit code is on its way. It expires shortly, and too many wrong
                  guesses will void it.
                </p>
              </div>

              <label className="block space-y-2">
                <span className="text-sm font-semibold">6-digit code</span>
                <input
                  required
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  pattern="\d{6}"
                  maxLength={6}
                  value={otp}
                  onChange={(e) => setOtp(e.target.value.replace(/\D/g, ""))}
                  placeholder="123456"
                  className={`${INPUT} text-center text-lg font-bold tracking-[0.4em]`}
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

              {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

              <button type="submit" disabled={busy} className={PRIMARY}>
                {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                Set new password
              </button>

              <button
                type="button"
                disabled={busy}
                onClick={() => {
                  setStep("request");
                  setOtp("");
                  setError(null);
                }}
                className="w-full text-center text-sm font-semibold text-primary transition hover:underline disabled:opacity-50"
              >
                Send a new code
              </button>
            </form>
          )}
        </section>
      </div>
    </main>
  );
}

const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-background px-4 text-sm text-foreground shadow-sm outline-none transition placeholder:text-muted-foreground focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2";

const PRIMARY =
  "inline-flex h-12 w-full items-center justify-center gap-2 rounded-2xl bg-primary px-4 text-sm font-semibold text-primary-foreground shadow-panel transition hover:bg-primary/90 disabled:pointer-events-none disabled:opacity-50";
