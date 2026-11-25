import { useState } from "react";
import { ArrowLeftRight, LoaderCircle } from "lucide-react";
import { switchPortal } from "../api";

// The account-menu row for someone who is both an employee and an admin here:
// one login, two portals. Shown only when the session says they can switch.
export function PortalSwitchMenuItem({ toAdmin }: { toAdmin: boolean }) {
  const [switching, setSwitching] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function go() {
    setSwitching(true);
    setError(null);
    try {
      await switchPortal();
      // Same as a company switch: reload so every screen starts clean.
      window.location.reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Couldn't switch portal.");
      setSwitching(false);
    }
  }

  return (
    <button
      type="button"
      onClick={() => void go()}
      disabled={switching}
      className="mt-2 flex w-full items-start gap-3 rounded-xl px-3 py-2.5 text-left text-sm text-muted-foreground transition hover:bg-muted disabled:opacity-60"
    >
      {switching ? (
        <LoaderCircle className="mt-0.5 h-4 w-4 shrink-0 animate-spin" />
      ) : (
        <ArrowLeftRight className="mt-0.5 h-4 w-4 shrink-0" />
      )}
      <span>
        <span className="block font-semibold text-foreground">
          {toAdmin ? "Switch to admin portal" : "Switch to my portal"}
        </span>
        <span className="block text-xs">
          {error ?? (toAdmin ? "Manage the company" : "Your own leave, claims, attendance and payslips")}
        </span>
      </span>
    </button>
  );
}
