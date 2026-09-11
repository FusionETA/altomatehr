import { useCallback, useEffect, useState } from "react";
import { ArrowLeft } from "lucide-react";
import { OverflowTabList } from "@/shared/components/OverflowTabList";
import { BUTTON_GHOST } from "../lib/ui";
import { getPayrollRuns, type PayrollRun } from "../api";
import { PayrollOverview } from "./PayrollOverview";
import { PayrollRunsList } from "./PayrollRunsList";
import { PayrollRunDetailView } from "./PayrollRunDetail";
import { PayrollEmployeesTab } from "./PayrollEmployeesTab";
import { PayrollLoansTab } from "./PayrollLoansTab";
import { PayrollAnnualTab } from "./PayrollAnnualTab";
import { PayrollSettingsForm } from "./PayrollSettingsForm";

// The payroll admin surface.
//
// Overview first — it is the way in, and it is where the engine explains how
// every statutory line is worked out. Then the tabs in the order they are
// needed: the monthly job, the loans it deducts, the year the runs add up to,
// and the settings that are configured once and rarely touched.
//
// EMPLOYEES is deliberately not a tab. Managing people already lives under
// Company/Employee in the sidebar, so a second entry point here read as a
// duplicate of it; what is payroll-specific — the statutory-gap roster and
// the bulk sheet — is reached from the Overview card instead. This is also
// how the reference app arranges it: a page, but not a nav item.
//
// Opening a run replaces the list rather than pushing a route — the admin
// shell is a single view switch, so the drill-down is local state and the
// back button is explicit.
type PayrollTab = "overview" | "runs" | "employees" | "loans" | "annual" | "settings";

export function AdminPayroll({
  // Handed down from the admin shell so the overview can send an admin to
  // Manage Employee, which already owns people, instead of duplicating it.
  onOpen,
}: {
  onOpen?: (parentId: string, childId: string) => void;
}) {
  const [tab, setTab] = useState<PayrollTab>("overview");
  const [runs, setRuns] = useState<PayrollRun[]>([]);
  const [openRunId, setOpenRunId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);

    return getPayrollRuns()
      .then(setRuns)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  // A draft awaiting nothing is not news; a run sitting in PENDING_APPROVAL
  // is someone waiting on this admin, so the tab carries that count.
  const awaitingApproval = runs.filter((run) => run.status === "PENDING_APPROVAL").length;

  return (
    <div className="space-y-6">
      <OverflowTabList<PayrollTab>
        items={[
          { id: "overview", label: "Overview" },
          { id: "runs", label: "Payroll runs", badge: awaitingApproval },
          { id: "loans", label: "Loans" },
          { id: "annual", label: "Annual forms" },
          { id: "settings", label: "Settings" },
        ]}
        value={tab}
        onChange={(next) => {
          setTab(next);
          // Leaving the tab abandons the drill-down, so coming back lands on
          // the list rather than a run the admin has stopped thinking about.
          setOpenRunId(null);
        }}
        className="sm:max-w-3xl sm:flex-1"
        ariaLabel="Payroll views"
      />

      {tab === "overview" ? (
        <PayrollOverview
          onGo={(key) => setTab(key as PayrollTab)}
          onOpen={onOpen}
        />
      ) : tab === "employees" ? (
        // Reached from the overview's "Statutory readiness" card, not from
        // the tab bar, so it needs its own way back — otherwise the only
        // exit is a tab that does not describe where you are.
        <div className="space-y-4">
          <button
            type="button"
            className={BUTTON_GHOST}
            onClick={() => setTab("overview")}
          >
            <ArrowLeft className="size-4" aria-hidden />
            Back to overview
          </button>
          <PayrollEmployeesTab />
        </div>
      ) : tab === "loans" ? (
        <PayrollLoansTab />
      ) : tab === "annual" ? (
        <PayrollAnnualTab />
      ) : tab === "settings" ? (
        <PayrollSettingsForm />
      ) : openRunId ? (
        <PayrollRunDetailView
          runId={openRunId}
          onBack={() => {
            setOpenRunId(null);
            // The run's status or totals may have moved while it was open.
            void load();
          }}
        />
      ) : (
        <PayrollRunsList
          runs={runs}
          loading={loading}
          error={error}
          onOpen={setOpenRunId}
          onCreated={() => void load()}
        />
      )}
    </div>
  );
}
