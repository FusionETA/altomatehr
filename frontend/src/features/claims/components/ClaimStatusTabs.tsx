import {
  claimStatusLabels,
  visibleClaimStatuses,
  type ClaimStatusFilter,
} from "../lib/claim-status";
import { StatusFilterTabs } from "@/shared/components/StatusFilterTabs";

type ClaimStatusTabsProps = {
  value: ClaimStatusFilter;
  onChange: (value: ClaimStatusFilter) => void;
  className?: string;
};

export function ClaimStatusTabs({ value, onChange, className = "" }: ClaimStatusTabsProps) {
  return (
    <StatusFilterTabs<ClaimStatusFilter>
      statuses={visibleClaimStatuses}
      labels={claimStatusLabels}
      value={value}
      onChange={onChange}
      className={className}
      ariaLabel="Claim status filters"
    />
  );
}
