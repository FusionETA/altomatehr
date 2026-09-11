import type {
  AdjustmentCategory,
  AdjustmentCategoryGroup,
  PayslipLineKind,
} from "../api";
import { PayrollSelect } from "./PayrollSelect";

// The category dropdown, grouped the way the catalogue is declared.
//
// Grouped, and searchable by virtue of being long — the shared Select turns
// on a filter box past seven options, which 46 categories across four
// headings very much needs.
const GROUP_HEADINGS: Record<AdjustmentCategoryGroup, string> = {
  ALLOWANCE: "Allowances",
  REMUNERATION: "Remuneration",
  BENEFIT_IN_KIND: "Benefits in kind",
  DEDUCTION: "Deductions",
};

const ORDER: AdjustmentCategoryGroup[] = [
  "ALLOWANCE",
  "REMUNERATION",
  "BENEFIT_IN_KIND",
  "DEDUCTION",
];

export function CategoryPicker({
  categories,
  value,
  kind,
  disabled,
  onChange,
}: {
  categories: AdjustmentCategory[];
  value: string;
  // Narrows the list to one side of the catalogue. An allowance row must
  // not be able to become a deduction by picking from the same dropdown —
  // that is what the add buttons decide.
  kind?: PayslipLineKind;
  disabled?: boolean;
  onChange: (code: string) => void;
}) {
  const offered = kind
    ? categories.filter((category) => category.kind === kind)
    : categories;

  return (
    <PayrollSelect
      value={value}
      disabled={disabled}
      ariaLabel="Category"
      onChange={(next) => next && onChange(next)}
      groups={ORDER.flatMap((group) => {
        const options = offered.filter((category) => category.group === group);
        if (options.length === 0) return [];

        return [
          {
            label: GROUP_HEADINGS[group],
            options: options.map((category) => ({
              value: category.code,
              label: category.label,
            })),
          },
        ];
      })}
    />
  );
}

// What the category does, as the reference states it: the kind, the bases
// it feeds, and the handful of behaviours that change the arithmetic.
//
// This is the whole reason the catalogue is served rather than guessed at —
// two allowances of the same size move the tax differently, and the only
// honest way to show that is to read the flags the calculator itself uses.
export function StatutoryStrip({
  category,
}: {
  category: AdjustmentCategory | undefined;
}) {
  if (!category) {
    return (
      <p className="mt-2 text-[11px] font-medium text-destructive">
        Unknown category — this row would be skipped at generation.
      </p>
    );
  }

  const bases = [
    category.subjectToEpf && "EPF",
    category.subjectToSocso && "SOCSO",
    category.subjectToEis && "EIS",
    category.subjectToPcb && "PCB",
    category.subjectToHrdf && "HRDF",
  ].filter(Boolean) as string[];

  // For a deduction that shrinks the bases, the flags mean it LOWERS those
  // bases — not that the row is subject to them. Saying "Statutory:" there
  // would state the opposite of what happens.
  const reducing = category.kind === "DEDUCTION" && category.reducesBase;

  const notes = [
    category.nonCash && "Not paid in cash, but taxable on Form EA",
    category.reducesGross && "Comes off gross, not take-home",
    category.cashNeutral && "Already paid by the employee — lowers PCB only",
    category.feedsLp1Relief && "A TP1 relief",
    category.addsToCp38Field && "Filed in CP39's CP38 column",
    category.offsetsPcb && "Offsets PCB ringgit for ringgit",
    category.taxExemptLimit !== null &&
      `Tax exempt up to RM ${category.taxExemptLimit.toLocaleString("en-MY")} a year`,
  ].filter(Boolean) as string[];

  return (
    <div className="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-[11px] text-muted-foreground">
      <span className="rounded-full bg-muted px-2 py-0.5 font-medium text-foreground">
        {category.kind === "DEDUCTION"
          ? reducing
            ? "Deduction — reduces base"
            : "Deduction"
          : "Earning"}
      </span>

      <span>
        {reducing ? "Reduces base for: " : "Statutory: "}
        {bases.length > 0 ? bases.join(", ") : "none"}
      </span>

      {notes.map((note) => (
        <span key={note}>· {note}</span>
      ))}

      {category.isAdditionalRemuneration ? (
        <span className="text-amber-700 dark:text-amber-400">
          · PCB: additional remuneration formula
        </span>
      ) : null}
    </div>
  );
}
