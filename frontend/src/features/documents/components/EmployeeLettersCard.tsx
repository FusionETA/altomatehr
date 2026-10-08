import { useState } from "react";
import { FilePlus2 } from "lucide-react";
import { useEnabledModules, useMyAccess } from "@/features/settings/lib/module-access";
import { GenerateLetterDialog } from "./GenerateLetterDialog";
import { GeneratedLettersList } from "./GeneratedLettersList";

const CARD = "rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";

// One employee's HR letters, on their admin record (Manage Employee →
// Documents). Separate from the uploaded documents above it on purpose: those
// the employee can see in their portal; these they can't.
//
// Hidden when the admin's access doesn't include the Documents module.
//
// A letter can save gaps into this employee's record, which the form around
// this card holds a copy of. So the host says when that would clash
// (`saveToEmployeeBlocked`, e.g. unsaved edits) and re-reads the record after
// it happened (`onSavedToEmployee`) — else its next Save writes the stale copy
// back over what the letter just saved.
export function EmployeeLettersCard({
  employeeUserId,
  employeeName,
  saveToEmployeeBlocked,
  onSavedToEmployee,
}: {
  employeeUserId: string;
  employeeName: string;
  saveToEmployeeBlocked?: string | null;
  onSavedToEmployee?: () => void;
}) {
  const enabled = useEnabledModules();
  const access = useMyAccess();
  const [generating, setGenerating] = useState(false);

  if (enabled !== null && !enabled.has("documents")) return null;
  const canManage = access.canManage("documents");

  return (
    <div className={`${CARD} space-y-4 p-4 sm:p-5`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="text-sm font-black text-foreground">Letters</h3>
          <p className="mt-0.5 text-xs text-muted-foreground">
            HR letters generated for {employeeName || "this employee"} from your templates. Admins only —
            not shown in the employee's portal.
          </p>
        </div>
        {canManage ? (
          <button
            type="button"
            onClick={() => setGenerating(true)}
            className="inline-flex h-9 shrink-0 items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-xs font-bold text-foreground transition hover:border-primary hover:text-primary"
          >
            <FilePlus2 className="h-3.5 w-3.5" />
            Generate letter
          </button>
        ) : null}
      </div>

      <GeneratedLettersList employeeUserId={employeeUserId} showEmployee={false} canManage={canManage} />

      {generating ? (
        <GenerateLetterDialog
          employeeUserId={employeeUserId}
          onClose={() => setGenerating(false)}
          saveToEmployeeBlocked={saveToEmployeeBlocked}
          onGenerated={({ savedToEmployee }) => {
            if (savedToEmployee.length > 0) onSavedToEmployee?.();
          }}
        />
      ) : null}
    </div>
  );
}
