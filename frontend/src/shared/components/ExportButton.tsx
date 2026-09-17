import { Download } from "lucide-react";

// The standard "download this as a file" control.
//
// Outlined rather than solid on purpose: an export is a utility, not the
// thing the screen is for, and a filled primary button next to a filter row
// reads as the main action when the list below it is.
//
// Lived in AdminAttendance until the employee and team exports needed the
// same thing; one definition so three screens can't drift into three shapes.
export function ExportButton({
  label,
  busy = false,
  disabled = false,
  onClick,
}: {
  label: string;
  busy?: boolean;
  disabled?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled || busy}
      className="inline-flex items-center gap-1.5 rounded-2xl border border-border/70 bg-card px-3.5 py-2 text-xs font-bold text-foreground shadow-sm transition-colors hover:bg-muted disabled:opacity-60"
    >
      <Download className="h-3.5 w-3.5" aria-hidden />
      {busy ? "Building…" : label}
    </button>
  );
}
