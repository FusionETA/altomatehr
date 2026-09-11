import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

// Every dropdown on the payroll screens.
//
// Payroll was built on bare `<select>` elements, which render the operating
// system's own arrow — a different shape on every platform, and nothing like
// the rest of the app. This is the same Radix control the accounts, policies
// and team screens already use, so the chevron matches and a list longer
// than seven options gets a search box for free. That matters here: the
// chart of accounts and the 46 adjustment categories are both well past it.
export type SelectOption = { value: string; label: string; disabled?: boolean };

export type SelectOptionGroup = { label: string; options: SelectOption[] };

// Radix reserves the empty string for "no selection", so an option that
// genuinely MEANS empty — "Not mapped", "No tracking" — needs a stand-in.
const EMPTY = "__payroll_none__";

export function PayrollSelect({
  id,
  value,
  onChange,
  options,
  groups,
  // The label for the null choice. Omit when the field is mandatory.
  emptyLabel,
  placeholder,
  disabled,
  ariaLabel,
  className,
}: {
  id?: string;
  value: string | null;
  onChange: (next: string | null) => void;
  options?: SelectOption[];
  groups?: SelectOptionGroup[];
  emptyLabel?: string;
  placeholder?: string;
  disabled?: boolean;
  ariaLabel?: string;
  className?: string;
}) {
  return (
    <Select
      value={value === null || value === "" ? EMPTY : value}
      onValueChange={(next) => onChange(next === EMPTY ? null : next)}
      disabled={disabled}
    >
      <SelectTrigger id={id} aria-label={ariaLabel} className={className}>
        <SelectValue placeholder={placeholder} />
      </SelectTrigger>

      <SelectContent>
        {emptyLabel ? <SelectItem value={EMPTY}>{emptyLabel}</SelectItem> : null}

        {options?.map((option) => (
          <SelectItem key={option.value} value={option.value} disabled={option.disabled}>
            {option.label}
          </SelectItem>
        ))}

        {groups?.map((group) => (
          <SelectGroup key={group.label}>
            <SelectLabel>{group.label}</SelectLabel>
            {group.options.map((option) => (
              <SelectItem key={option.value} value={option.value} disabled={option.disabled}>
                {option.label}
              </SelectItem>
            ))}
          </SelectGroup>
        ))}
      </SelectContent>
    </Select>
  );
}
