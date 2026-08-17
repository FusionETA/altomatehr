import {
  claimStatusLabels,
  visibleClaimStatuses,
  type ClaimStatusFilter,
} from "../lib/claim-status";
import { HorizontalScrollArea } from "@/shared/components/HorizontalScrollArea";

type ClaimStatusTabsProps = {
  value: ClaimStatusFilter;
  onChange: (value: ClaimStatusFilter) => void;
  className?: string;
};

export function ClaimStatusTabs({ value, onChange, className = "" }: ClaimStatusTabsProps) {
  const tabs: { value: ClaimStatusFilter; label: string }[] = [
    { value: "ALL", label: "All" },
    ...visibleClaimStatuses.map((status) => ({
      value: status,
      label: claimStatusLabels[status],
    })),
  ];

  return (
    <HorizontalScrollArea
      className={className}
      contentClassName="inline-flex items-center gap-1 rounded-xl border border-border/60 bg-surface-low p-1"
    >
      {tabs.map((tab) => {
        const active = tab.value === value;
        return (
          <button
            key={tab.value}
            type="button"
            onClick={() => onChange(tab.value)}
            className={`h-9 min-w-[76px] rounded-lg px-3 text-sm font-bold transition-colors ${
              active
                ? "bg-card text-primary shadow-sm"
                : "text-muted-foreground hover:bg-card/70 hover:text-foreground"
            }`}
          >
            {tab.label}
          </button>
        );
      })}
    </HorizontalScrollArea>
  );
}
