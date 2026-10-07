import { Lock } from "lucide-react";

// Shown above company configuration when the signed-in admin's access leaves
// out "Change settings". The form below is disabled; the server refuses the
// save regardless.
export function SettingsReadOnlyNote() {
  return (
    <p className="flex items-start gap-2 rounded-2xl border border-border/60 bg-muted/40 px-4 py-3 text-sm text-muted-foreground">
      <Lock className="mt-0.5 h-4 w-4 shrink-0" />
      <span>
        View only — your admin access doesn't include changing company settings. Ask the
        organization's owner if something here needs to change.
      </span>
    </p>
  );
}
