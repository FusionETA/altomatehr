import { leaveStatusLabels, visibleLeaveStatuses, type LeaveStatusFilter } from "../lib/leave-status";
import { StatusFilterTabs } from "@/shared/components/StatusFilterTabs";

type LeaveStatusTabsProps = {
  value: LeaveStatusFilter;
  onChange: (value: LeaveStatusFilter) => void;
  className?: string;
};

export function LeaveStatusTabs({ value, onChange, className = "" }: LeaveStatusTabsProps) {
  return (
    <StatusFilterTabs<LeaveStatusFilter>
      statuses={visibleLeaveStatuses}
      labels={leaveStatusLabels}
      value={value}
      onChange={onChange}
      className={className}
      ariaLabel="Leave status filters"
    />
  );
}
