import { useState, type ButtonHTMLAttributes, type ReactNode } from "react";
import { Check, Copy, KeyRound, LoaderCircle } from "lucide-react";

// A person's payslip password — one for all their payslip PDFs: hidden until
// asked for, then shown with a copy button. Fetched on request, so just
// opening the page doesn't pull it.
//
// Shared by the employee's Payslips page and the admin run table; `load` is
// the fetch for whichever side it's on. `initialPassword` shows one already
// in hand (an admin's reset returns the new one); remount with a new `key` to
// start hidden again. `children` are extra buttons after Show / Copy.
export function PayslipPasswordRow({
  load,
  label = "PDF password",
  initialPassword = null,
  className = "",
  children,
}: {
  load: () => Promise<{ password: string }>;
  label?: string;
  initialPassword?: string | null;
  className?: string;
  children?: ReactNode;
}) {
  const [password, setPassword] = useState<string | null>(initialPassword);
  const [loading, setLoading] = useState(false);
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function show() {
    if (password !== null) {
      setPassword(null);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      setPassword((await load()).password);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Could not show the password.");
    } finally {
      setLoading(false);
    }
  }

  async function copy() {
    if (password === null) return;
    try {
      await navigator.clipboard.writeText(password);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard refused (no permission / insecure origin): it's on screen to type.
    }
  }

  return (
    <div className={`flex flex-wrap items-center gap-2 text-xs ${className}`}>
      <KeyRound aria-hidden className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
      <span className="text-muted-foreground">{label}</span>
      <span className="break-all font-mono font-semibold tracking-wider text-foreground">
        {password ?? "••••••••"}
      </span>
      <button type="button" onClick={() => void show()} disabled={loading} className={PILL}>
        {loading ? <LoaderCircle aria-hidden className="h-3 w-3 animate-spin" /> : null}
        {password === null ? "Show" : "Hide"}
      </button>
      {password !== null ? (
        <button
          type="button"
          onClick={() => void copy()}
          aria-label="Copy the PDF password"
          className={PILL}
        >
          {copied ? <Check aria-hidden className="h-3 w-3" /> : <Copy aria-hidden className="h-3 w-3" />}
          {copied ? "Copied" : "Copy"}
        </button>
      ) : null}
      {children}
      {error ? <span className="basis-full font-medium text-destructive">{error}</span> : null}
    </div>
  );
}

// An extra button in the row (Change, Reset to random), in the row's look.
export function PayslipPasswordButton({
  className = "",
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement>) {
  return <button type="button" {...props} className={`${PILL} ${className}`} />;
}

const PILL =
  "inline-flex items-center gap-1 rounded-full border border-border/60 bg-card px-2.5 py-0.5 font-semibold text-muted-foreground transition-colors hover:text-foreground disabled:opacity-50";
