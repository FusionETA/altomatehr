import type { ReactNode } from "react";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

export const NONE = "__none__";

const INPUT =
  "h-11 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm outline-none transition focus-visible:ring-2 focus-visible:ring-primary";

// The parts every profile section is built from.
//
// Shared so five sections of a 67-field form can't drift into five slightly
// different label sizes and input heights — the thing that makes a long form
// feel unfinished even when every field works.

// A titled block within a section. Sub-grouping is what makes 13 statutory
// fields readable: EPF, SOCSO and income tax are separate concerns that happen
// to live on one record.
export function Group({
  title,
  hint,
  children,
  columns = 2,
}: {
  title: string;
  hint?: string;
  children: ReactNode;
  columns?: 1 | 2 | 3;
}) {
  const cols =
    columns === 3 ? "sm:grid-cols-3" : columns === 1 ? "grid-cols-1" : "sm:grid-cols-2";
  return (
    <section className="rounded-2xl border border-border/60 bg-surface-low/40 p-4 sm:p-5">
      <h3 className="text-sm font-black text-foreground">{title}</h3>
      {hint ? <p className="mt-0.5 text-xs text-muted-foreground">{hint}</p> : null}
      <div className={`mt-4 grid gap-4 ${cols}`}>{children}</div>
    </section>
  );
}

export function Field({
  label,
  hint,
  span,
  children,
}: {
  label: string;
  hint?: string;
  /** Full-width inside its grid. */
  span?: boolean;
  children: ReactNode;
}) {
  return (
    <label className={`grid gap-1.5 ${span ? "sm:col-span-full" : ""}`}>
      <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">
        {label}
      </span>
      {children}
      {hint ? <span className="text-xs text-muted-foreground">{hint}</span> : null}
    </label>
  );
}

export function Text({
  value,
  onChange,
  type = "text",
  placeholder,
}: {
  value: string | null;
  onChange: (value: string | null) => void;
  type?: "text" | "email" | "date" | "tel";
  placeholder?: string;
}) {
  return (
    <input
      type={type}
      placeholder={placeholder}
      // Dates arrive as ISO datetimes; <input type="date"> wants yyyy-MM-dd.
      value={type === "date" ? (value ?? "").slice(0, 10) : (value ?? "")}
      onChange={(e) => onChange(e.target.value || null)}
      className={INPUT}
    />
  );
}

// Money, with the unit shown rather than assumed. Everything is ringgit today.
export function Money({
  value,
  onChange,
}: {
  value: number | null;
  onChange: (value: number | null) => void;
}) {
  return (
    <div className="relative">
      <span className="pointer-events-none absolute left-4 top-1/2 -translate-y-1/2 text-sm font-semibold text-muted-foreground">
        RM
      </span>
      <input
        type="number"
        min={0}
        step="0.01"
        value={value ?? ""}
        onChange={(e) => onChange(e.target.value === "" ? null : Number(e.target.value))}
        className={`${INPUT} pl-11 text-right tabular-nums`}
      />
    </div>
  );
}

// A rate stored as a FRACTION but entered as a percentage.
//
// The wire carries 0.11 for 11%. Without this an admin types 11, the API
// stores 11, and the employee's EPF becomes 1100% of salary — a mistake with
// no visible symptom until payroll runs.
export function Percent({
  value,
  onChange,
}: {
  value: number;
  onChange: (value: number) => void;
}) {
  return (
    <div className="relative">
      <input
        type="number"
        min={0}
        max={100}
        step="0.5"
        value={round2(value * 100)}
        onChange={(e) => onChange(Number(e.target.value || 0) / 100)}
        className={`${INPUT} pr-9 text-right tabular-nums`}
      />
      <span className="pointer-events-none absolute right-4 top-1/2 -translate-y-1/2 text-sm font-semibold text-muted-foreground">
        %
      </span>
    </div>
  );
}

function round2(n: number) {
  return Math.round(n * 100) / 100;
}

export function Picker<T extends string>({
  value,
  onChange,
  options,
  placeholder,
  allowNone = false,
  noneLabel = "Not set",
}: {
  value: T | null;
  onChange: (value: T | null) => void;
  options: { value: T; label: string }[];
  placeholder?: string;
  allowNone?: boolean;
  noneLabel?: string;
}) {
  return (
    <Select
      value={value ?? NONE}
      onValueChange={(v) => onChange(v === NONE ? null : (v as T))}
    >
      <SelectTrigger className="h-11 bg-card">
        <SelectValue placeholder={placeholder} />
      </SelectTrigger>
      <SelectContent>
        {allowNone ? <SelectItem value={NONE}>{noneLabel}</SelectItem> : null}
        {options.map((o) => (
          <SelectItem key={o.value} value={o.value}>
            {o.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

// A switch reads better than a checkbox for "does this apply", and the
// consequence line matters: several of these change what payroll deducts.
export function Toggle({
  label,
  hint,
  checked,
  onChange,
}: {
  label: string;
  hint?: string;
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <label className="flex cursor-pointer items-start justify-between gap-3 rounded-2xl border border-border/60 bg-card px-4 py-3 transition hover:border-primary/30">
      <span className="min-w-0">
        <span className="block text-sm font-bold text-foreground">{label}</span>
        {hint ? <span className="mt-0.5 block text-xs text-muted-foreground">{hint}</span> : null}
      </span>
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
        className="mt-0.5 h-4 w-4 shrink-0 rounded border-border accent-primary"
      />
    </label>
  );
}

// Tri-state, because "we don't know yet" and "no" are different answers for a
// spouse's employment — and one of them affects the employee's PCB.
export function TriToggle({
  label,
  value,
  onChange,
}: {
  label: string;
  value: boolean | null;
  onChange: (value: boolean | null) => void;
}) {
  const options: { value: string; label: string }[] = [
    { value: NONE, label: "Unknown" },
    { value: "yes", label: "Yes" },
    { value: "no", label: "No" },
  ];
  return (
    <Field label={label}>
      <div className="flex gap-1 rounded-full bg-muted p-1">
        {options.map((o) => {
          const active =
            (o.value === NONE && value === null) ||
            (o.value === "yes" && value === true) ||
            (o.value === "no" && value === false);
          return (
            <button
              key={o.value}
              type="button"
              onClick={() => onChange(o.value === NONE ? null : o.value === "yes")}
              className={`flex-1 rounded-full px-3 py-2 text-xs font-bold transition ${
                active ? "bg-card text-primary shadow-sm" : "text-muted-foreground hover:text-foreground"
              }`}
            >
              {o.label}
            </button>
          );
        })}
      </div>
    </Field>
  );
}

// A plain whole number (a child's age, a count). Not money, not a percentage.
export function Num({
  value,
  onChange,
  min = 0,
  max,
  placeholder,
}: {
  value: number | null;
  onChange: (value: number | null) => void;
  min?: number;
  max?: number;
  placeholder?: string;
}) {
  return (
    <input
      type="number"
      min={min}
      max={max}
      step="1"
      placeholder={placeholder}
      value={value ?? ""}
      onChange={(e) => onChange(e.target.value === "" ? null : Number(e.target.value))}
      className={`${INPUT} text-right tabular-nums`}
    />
  );
}

/** One row of a repeating list, with its own remove control. */
export function RepeaterRow({
  onRemove,
  removeLabel,
  children,
}: {
  onRemove: () => void;
  removeLabel: string;
  children: ReactNode;
}) {
  return (
    <div className="rounded-2xl border border-border/60 bg-card p-4">
      <div className="grid gap-4 sm:grid-cols-2">{children}</div>
      <button
        type="button"
        onClick={onRemove}
        className="mt-3 text-xs font-bold text-destructive transition hover:underline"
      >
        {removeLabel}
      </button>
    </div>
  );
}

/** A titled block whose children are stacked rows rather than a field grid. */
export function Stack({
  title,
  hint,
  children,
  action,
}: {
  title: string;
  hint?: string;
  children: ReactNode;
  action?: ReactNode;
}) {
  return (
    <section className="rounded-2xl border border-border/60 bg-surface-low/40 p-4 sm:p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-black text-foreground">{title}</h3>
          {hint ? <p className="mt-0.5 text-xs text-muted-foreground">{hint}</p> : null}
        </div>
        {action}
      </div>
      <div className="mt-4 space-y-3">{children}</div>
    </section>
  );
}
