import { useState } from "react";
import { Check, Copy, KeyRound, LoaderCircle } from "lucide-react";

// One payslip's own PDF password (the RANDOM payslip-password rule): hidden
// until asked for, then shown with a copy button. Fetched on request, so just
// opening the page doesn't pull every password.
//
// Shared by the employee's Payslips page and the admin run table; `load` is
// the fetch for whichever side it's on.
export function PayslipPasswordRow({
  load,
  className = "",
}: {
  load: () => Promise<{ password: string }>;
  className?: string;
}) {
  const [password, setPassword] = useState<string | null>(null);
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
      <span className="text-muted-foreground">PDF password</span>
      <span className="font-mono font-semibold tracking-wider text-foreground">
        {password ?? "••••-••••"}
      </span>
      <button
        type="button"
        onClick={() => void show()}
        disabled={loading}
        className="inline-flex items-center gap-1 rounded-full border border-border/60 bg-card px-2.5 py-0.5 font-semibold text-muted-foreground transition-colors hover:text-foreground disabled:opacity-50"
      >
        {loading ? <LoaderCircle aria-hidden className="h-3 w-3 animate-spin" /> : null}
        {password === null ? "Show" : "Hide"}
      </button>
      {password !== null ? (
        <button
          type="button"
          onClick={() => void copy()}
          aria-label="Copy the PDF password"
          className="inline-flex items-center gap-1 rounded-full border border-border/60 bg-card px-2.5 py-0.5 font-semibold text-muted-foreground transition-colors hover:text-foreground"
        >
          {copied ? <Check aria-hidden className="h-3 w-3" /> : <Copy aria-hidden className="h-3 w-3" />}
          {copied ? "Copied" : "Copy"}
        </button>
      ) : null}
      {error ? <span className="basis-full font-medium text-destructive">{error}</span> : null}
    </div>
  );
}
