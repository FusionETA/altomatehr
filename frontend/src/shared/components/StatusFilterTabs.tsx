import { OverflowTabList } from "./OverflowTabList";

type StatusFilterTabsProps<T extends string> = {
  value: T;
  onChange: (value: T) => void;
  statuses: readonly T[];
  labels: Partial<Record<T, string>>;
  className?: string;
  ariaLabel?: string;
  allValue?: T;
  allLabel?: string;
};

export function StatusFilterTabs<T extends string>({
  value,
  onChange,
  statuses,
  labels,
  className,
  ariaLabel = "Status filters",
  allValue = "ALL" as T,
  allLabel = "All",
}: StatusFilterTabsProps<T>) {
  const items = [
    { id: allValue, label: allLabel },
    ...statuses.map((status) => ({
      id: status,
      label: labels[status] ?? status,
    })),
  ];

  return (
    <OverflowTabList
      items={items}
      value={value}
      onChange={onChange}
      variant="segmented"
      className={className}
      ariaLabel={ariaLabel}
    />
  );
}
