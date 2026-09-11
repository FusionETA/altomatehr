import { useState } from "react";

// Payslips and what is in the way, as two tabs rather than one long stack.
//
// The figures are what an admin opens the run to read; burying them under
// three warning cards means scrolling past the problem list every time,
// including on the months where it is empty. The count on the tab is enough
// to say something needs looking at.
export function PayrollRunTabs({
  payslips,
  attention,
  payslipCount,
  attentionCount,
}: {
  payslips: React.ReactNode;
  attention: React.ReactNode;
  payslipCount: number;
  attentionCount: number;
}) {
  const [tab, setTab] = useState<"payslips" | "attention">("payslips");

  // Nothing to attend to — no need for a chooser with one real option.
  if (attentionCount === 0) return <>{payslips}</>;

  return (
    <div className="space-y-4">
      <div role="tablist" aria-label="Run sections" className="flex flex-wrap gap-2">
        <Tab
          active={tab === "payslips"}
          onClick={() => setTab("payslips")}
          label="Payslips"
          count={payslipCount}
        />
        <Tab
          active={tab === "attention"}
          onClick={() => setTab("attention")}
          label="Needs attention"
          count={attentionCount}
          accent
        />
      </div>

      {/* Both panels stay mounted and are toggled with `hidden`. Switching
          tabs must not throw away a half-scrolled table or refetch it. */}
      <div className={tab === "payslips" ? "block" : "hidden"}>{payslips}</div>
      <div className={tab === "attention" ? "block" : "hidden"}>{attention}</div>
    </div>
  );
}

function Tab({
  active,
  onClick,
  label,
  count,
  accent,
}: {
  active: boolean;
  onClick: () => void;
  label: string;
  count: number;
  accent?: boolean;
}) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      onClick={onClick}
      className={`flex shrink-0 items-center gap-2 rounded-full border px-4 py-1.5 text-xs font-semibold transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background ${
        active
          ? "border-primary bg-primary text-primary-foreground"
          : "border-border/60 bg-card text-muted-foreground hover:text-foreground"
      }`}
    >
      {label}
      <span
        className={`rounded-full px-1.5 py-0.5 text-[10px] font-semibold tabular-nums ${
          active
            ? "bg-primary-foreground/20 text-primary-foreground"
            : accent
              ? "bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-300"
              : "bg-muted text-muted-foreground"
        }`}
      >
        {count}
      </span>
    </button>
  );
}
