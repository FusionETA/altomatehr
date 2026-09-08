import { useEffect, useMemo, useState } from "react";
import { ArrowLeft, Check, CircleAlert, LoaderCircle, Plus, RotateCcw } from "lucide-react";
import {
  CHILD_ABILITY,
  CHILD_ABILITY_LABELS,
  CHILD_DEDUCTION,
  CHILD_DEDUCTION_LABELS,
  CHILD_STUDYING,
  CHILD_STUDYING_LABELS,
  GENDERS,
  ID_TYPES,
  ID_TYPE_LABELS,
  MARITAL_STATUSES,
  PAYMENT_METHODS,
  PAYMENT_METHOD_LABELS,
  STAFF_ROLES,
  SALARY_TYPES,
  SOCSO_SCHEMES,
  SOCSO_SCHEME_LABELS,
  getEmployeeProfile,
  parseChildRelief,
  parseFixedAllowances,
  saveEmployeeProfile,
  serializeList,
  toUpdateEmployee,
  updateEmployee,
  type ChildRelief,
  type Employee,
  type EmployeeProfile,
  type FixedAllowance,
} from "../api";
import type { Policy } from "@/features/policies/api";
import {
  DEFAULT_CATEGORY,
  categoriesFor,
  kindOf,
  labelForCategory,
  type AdjustmentKind,
} from "../lib/payroll-adjustments";
import { OverflowTabList } from "@/shared/components/OverflowTabList";
import {
  Field,
  Group,
  Money,
  NONE,
  Num,
  Percent,
  Picker,
  RepeaterRow,
  Stack,
  Text,
  Toggle,
  TriToggle,
} from "./profile-fields";
import {
  isReadyForPayroll,
  missingFields,
  type SectionId,
} from "./employee-profile-sections";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";

// The same four the previous system uses, holding the same cards: who the
// person is, what they're paid, what's deducted, and where they sit here.
const SECTIONS: { id: SectionId; label: string }[] = [
  { id: "personal", label: "Personal" },
  { id: "employment", label: "Employment" },
  { id: "statutory", label: "Statutory" },
  { id: "company", label: "Company" },
];

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

/** "" from an empty input means "no value", which the API spells null. */
function blank(value: string) {
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : trimmed;
}

// The fields that live on the org membership rather than the HR profile. They
// are edited here alongside it, but travel on a different endpoint.
type Placement = {
  role: string;
  supervisorId: string;
  policyId: string;
  name: string;
  employeeNumber: string;
  jobTitle: string;
  joinDate: string;
};

// One employee's whole record.
//
// A 67-field profile can't be a single scrolling form, and it can't be five
// disconnected tabs either — an admin needs to know what's still missing
// without opening each one. So: a section rail carrying per-section
// completeness, one header that stays put, and a save bar that only appears
// once something has actually changed.
export function EmployeeDetail({
  employee,
  policies,
  employees,
  onBack,
  onSaved,
}: {
  employee: Employee;
  policies: Policy[];
  employees: Employee[];
  onBack: () => void;
  onSaved: (updated: Employee) => void;
}) {
  const [section, setSection] = useState<SectionId>("personal");
  const [profile, setProfile] = useState<EmployeeProfile | null>(null);
  // The last-saved state, to tell "changed" from "loaded".
  const [baseline, setBaseline] = useState<EmployeeProfile | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const initialPlacement: Placement = {
    role: employee.role,
    supervisorId: employee.supervisorId ?? NONE,
    policyId: employee.policyId ?? NONE,
    name: employee.name,
    employeeNumber: employee.employeeNumber ?? "",
    jobTitle: employee.jobTitle ?? "",
    joinDate: employee.joinDate?.slice(0, 10) ?? "",
  };
  const [placement, setPlacement] = useState<Placement>(initialPlacement);
  const [placementBase, setPlacementBase] = useState<Placement>(initialPlacement);

  useEffect(() => {
    setLoading(true);
    setError(null);
    getEmployeeProfile(employee.id)
      .then((p) => {
        setProfile(p);
        setBaseline(p);
        // Two join dates existed before this screen did: one on the membership
        // (which pro-rates leave) and one on the profile. Where only the
        // profile has a value, show it rather than an empty field — the first
        // save then writes it to both and they stop disagreeing.
        if (!employee.joinDate && p.joinDate) {
          const seeded = p.joinDate.slice(0, 10);
          setPlacement((cur) => ({ ...cur, joinDate: seeded }));
          setPlacementBase((cur) => ({ ...cur, joinDate: seeded }));
        }
      })
      .catch((e: unknown) => setError(message(e, "Could not load this employee's profile.")))
      .finally(() => setLoading(false));
  }, [employee.id, employee.joinDate]);

  // Cheap and honest: the payload is flat, so a stringify comparison catches
  // any edit without maintaining a per-field dirty map that would drift as
  // sections are added.
  const dirty =
    (profile !== null && baseline !== null && JSON.stringify(profile) !== JSON.stringify(baseline)) ||
    JSON.stringify(placement) !== JSON.stringify(placementBase);

  // Anyone who could actually approve for this person. Admins and owners are
  // excluded because the backend's router skips administrative approvers —
  // offering one here would file requests into a step nobody can action.
  const supervisorOptions = useMemo(
    () =>
      employees.filter(
        (e) => e.id !== employee.id && (e.role === "Supervisor" || e.role === "Employee"),
      ),
    [employees, employee.id],
  );
  const activePolicies = useMemo(() => policies.filter((p) => !p.isArchived), [policies]);

  function set<K extends keyof EmployeeProfile>(key: K, value: EmployeeProfile[K]) {
    setProfile((current) => (current ? { ...current, [key]: value } : current));
  }

  // Both lists live on the profile as a JSON string. Editing them as arrays
  // and serializing on every change keeps a single source of truth — the dirty
  // check and the save payload stay exactly what they were.
  const children = parseChildRelief(profile?.childReliefJson);
  const allowances = parseFixedAllowances(profile?.fixedAllowancesJson);

  const setChildren = (rows: ChildRelief[]) => set("childReliefJson", serializeList(rows));
  const patchChild = (index: number, patch: Partial<ChildRelief>) =>
    setChildren(children.map((c, i) => (i === index ? { ...c, ...patch } : c)));

  const setAllowances = (rows: FixedAllowance[]) =>
    set("fixedAllowancesJson", serializeList(rows));
  const patchAllowance = (index: number, patch: Partial<FixedAllowance>) =>
    setAllowances(allowances.map((a, i) => (i === index ? { ...a, ...patch } : a)));

  function addAdjustment(kind: AdjustmentKind) {
    const category = DEFAULT_CATEGORY[kind];
    setAllowances([
      ...allowances,
      { category, name: labelForCategory(category), amount: null },
    ]);
  }

  function discard() {
    setProfile(baseline);
    setPlacement(placementBase);
    setError(null);
  }

  async function handleSave() {
    if (!profile) return;
    setSaving(true);
    setError(null);
    try {
      // Spread the current membership underneath: this PUT replaces the record,
      // so anything omitted (employee number, job title, shift, module grants)
      // would be written as null.
      const updated = await updateEmployee(employee.id, {
        ...toUpdateEmployee(employee),
        role: placement.role,
        supervisorId: placement.supervisorId === NONE ? null : placement.supervisorId,
        policyId: placement.policyId === NONE ? null : placement.policyId,
        name: blank(placement.name) ?? employee.name,
        employeeNumber: blank(placement.employeeNumber),
        jobTitle: blank(placement.jobTitle),
        joinDate: blank(placement.joinDate),
      });
      // The whole profile goes back: PUT replaces the record, so a partial
      // payload would null every field the other sections own.
      const savedProfile = await saveEmployeeProfile(employee.id, profile);

      setProfile(savedProfile);
      setBaseline(savedProfile);
      setPlacementBase(placement);
      onSaved(updated);
    } catch (e: unknown) {
      setError(message(e, "Could not save this employee."));
    } finally {
      setSaving(false);
    }
  }

  const name = placement.name.trim() || profile?.name?.trim() || employee.email;
  const ready = profile ? isReadyForPayroll(profile) : false;
  const sectionGaps = profile ? missingFields(profile, section) : [];

  return (
    <div className="space-y-4 pb-24">
      <button
        type="button"
        onClick={onBack}
        className="inline-flex items-center gap-1.5 text-sm font-semibold text-muted-foreground transition hover:text-foreground"
      >
        <ArrowLeft className="h-4 w-4" />
        All employees
      </button>

      {/* Identity header. Kept separate from the form so it stays readable
          while scrolling a long section. */}
      <section className={`${CARD} p-5 sm:p-6`}>
        <div className="flex flex-wrap items-center gap-4">
          <div className="min-w-0 flex-1 basis-full sm:basis-0">
            <div className="flex flex-wrap items-center gap-2">
              <h2 className="truncate text-lg font-black text-foreground sm:text-xl">{name}</h2>
              {placement.employeeNumber.trim() ? (
                <span className="text-sm font-semibold text-muted-foreground">
                  {placement.employeeNumber}
                </span>
              ) : null}
              <span className="rounded-full bg-muted px-2.5 py-1 text-[11px] font-bold text-muted-foreground">
                {placement.role}
              </span>
            </div>
            <p className="truncate text-sm text-muted-foreground">
              {[placement.jobTitle.trim(), employee.email].filter(Boolean).join(" · ")}
            </p>
          </div>

          {/* The verdict, in the same place the previous system puts it: one
              answer to "can I run payroll for this person yet?" */}
          {profile ? (
            profile.isArchived ? (
              <span className="inline-flex shrink-0 items-center gap-1.5 rounded-full border border-border bg-muted px-3 py-1.5 text-xs font-bold text-muted-foreground">
                Archived
              </span>
            ) : ready ? (
              <span className="inline-flex shrink-0 items-center gap-1.5 rounded-full border border-success/30 bg-success/10 px-3 py-1.5 text-xs font-bold text-success">
                <Check className="h-3.5 w-3.5" />
                Ready for payroll
              </span>
            ) : (
              <span className="inline-flex shrink-0 items-center gap-1.5 rounded-full bg-warning px-3 py-1.5 text-xs font-bold text-warning-foreground">
                <CircleAlert className="h-3.5 w-3.5" />
                Needs setup
              </span>
            )
          ) : null}
        </div>
      </section>

      {error ? (
        <p className="rounded-2xl border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm font-medium text-destructive">
          {error}
        </p>
      ) : null}

      {loading ? (
        <section className={`${CARD} p-6 text-sm text-muted-foreground`}>Loading profile…</section>
      ) : !profile ? null : (
        <div className="space-y-4">
          {/* The count is what's still missing in that section; a finished one
              carries no badge at all. */}
          <OverflowTabList<SectionId>
            items={SECTIONS.map((sec) => ({
              id: sec.id,
              label: sec.label,
              badge: missingFields(profile, sec.id).length,
            }))}
            value={section}
            onChange={setSection}
            variant="underline"
            className="border-b border-border/50"
            ariaLabel="Profile sections"
          />

          <div className="space-y-4">
            {/* What this section still needs, spelled out — the pill's count
                tells you there's a gap, this tells you which field. */}
            {sectionGaps.length > 0 ? (
              <p className="flex items-start gap-2 rounded-2xl border border-warning bg-warning/40 px-4 py-3 text-sm font-medium text-warning-foreground">
                <CircleAlert className="mt-0.5 h-4 w-4 shrink-0" />
                <span>
                  Still needed here: <strong>{sectionGaps.join(", ")}</strong>
                </span>
              </p>
            ) : null}

            {section === "personal" ? (
              <>
                <Group title="Contact">
                  <Field label="Phone">
                    <Text type="tel" value={profile.phone} onChange={(v) => set("phone", v)} />
                  </Field>
                  <Field label="Alternate email">
                    <Text
                      type="email"
                      value={profile.alternateEmail}
                      onChange={(v) => set("alternateEmail", v)}
                    />
                  </Field>
                </Group>

                <Group title="Identity">
                  <Field label="Gender">
                    <Picker
                      value={profile.gender}
                      onChange={(v) => set("gender", v)}
                      allowNone
                      placeholder="Not set"
                      options={GENDERS.map((g) => ({
                        value: g,
                        label: g === "MALE" ? "Male" : "Female",
                      }))}
                    />
                  </Field>
                  <Field label="Date of birth">
                    <Text
                      type="date"
                      value={profile.dateOfBirth}
                      onChange={(v) => set("dateOfBirth", v)}
                    />
                  </Field>
                  <Field label="ID type">
                    <Picker
                      value={profile.idType}
                      onChange={(v) => set("idType", v)}
                      allowNone
                      placeholder="Not set"
                      options={ID_TYPES.map((t) => ({ value: t, label: ID_TYPE_LABELS[t] }))}
                    />
                  </Field>
                  <Field label="ID number">
                    <Text value={profile.idNumber} onChange={(v) => set("idNumber", v)} />
                  </Field>
                  <Field label="Nationality">
                    <Text value={profile.nationality} onChange={(v) => set("nationality", v)} />
                  </Field>
                  <Field label="Race">
                    <Text value={profile.race} onChange={(v) => set("race", v)} />
                  </Field>
                  <Field label="Marital status">
                    <Picker
                      value={profile.maritalStatus}
                      onChange={(v) => set("maritalStatus", v)}
                      allowNone
                      placeholder="Not set"
                      options={MARITAL_STATUSES.map((m) => ({
                        value: m,
                        label: m.charAt(0) + m.slice(1).toLowerCase(),
                      }))}
                    />
                  </Field>
                </Group>

                {/* Together because all three change statutory treatment, and
                    reading them side by side is how a wrong one gets caught. */}
                <Group title="Status" hint="These affect what payroll deducts." columns={3}>
                  <Toggle
                    label="Malaysian PR"
                    checked={profile.hasPr}
                    onChange={(v) => set("hasPr", v)}
                  />
                  <Toggle
                    label="Tax resident"
                    hint="Non-residents are taxed at a flat rate."
                    checked={profile.isResident}
                    onChange={(v) => set("isResident", v)}
                  />
                  <Toggle
                    label="OKU"
                    hint="Registered disability; carries extra relief."
                    checked={profile.isOku}
                    onChange={(v) => set("isOku", v)}
                  />
                </Group>

                <Group title="Address" columns={3}>
                  <Field label="City">
                    <Text value={profile.city} onChange={(v) => set("city", v)} />
                  </Field>
                  <Field label="Postcode">
                    <Text value={profile.postcode} onChange={(v) => set("postcode", v)} />
                  </Field>
                  <Field label="State">
                    <Text value={profile.state} onChange={(v) => set("state", v)} />
                  </Field>
                </Group>

                <Group title="Emergency contact" columns={3}>
                  <Field label="Name">
                    <Text
                      value={profile.emergencyContactName}
                      onChange={(v) => set("emergencyContactName", v)}
                    />
                  </Field>
                  <Field label="Phone">
                    <Text
                      type="tel"
                      value={profile.emergencyContactPhone}
                      onChange={(v) => set("emergencyContactPhone", v)}
                    />
                  </Field>
                  <Field label="Relationship">
                    <Text
                      value={profile.emergencyContactRelation}
                      onChange={(v) => set("emergencyContactRelation", v)}
                    />
                  </Field>
                </Group>

                <Group
                  title="Spouse"
                  hint="Used for the employee's own PCB relief, not the spouse's."
                >
                  <TriToggle
                    label="Spouse working"
                    value={profile.spouseWorking}
                    onChange={(v) => set("spouseWorking", v)}
                  />
                  <TriToggle
                    label="Spouse disabled"
                    value={profile.spouseDisabled}
                    onChange={(v) => set("spouseDisabled", v)}
                  />
                  <Field label="Spouse ID number">
                    <Text
                      value={profile.spouseIdNumber}
                      onChange={(v) => set("spouseIdNumber", v)}
                    />
                  </Field>
                  <Field label="Spouse PCB number">
                    <Text
                      value={profile.spousePcbNumber}
                      onChange={(v) => set("spousePcbNumber", v)}
                    />
                  </Field>
                </Group>

                <Stack
                  title="Dependent children"
                  hint="Each child adds PCB child relief. Half applies when the other parent claims the rest."
                  action={
                    <button
                      type="button"
                      onClick={() =>
                        setChildren([
                          ...children,
                          {
                            age: null,
                            abilityStatus: "NORMAL",
                            currentlyStudying: "NONE",
                            pcbDeduction: "FULL",
                          },
                        ])
                      }
                      className="inline-flex h-9 items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-xs font-bold text-foreground transition hover:border-primary hover:text-primary"
                    >
                      <Plus className="h-3.5 w-3.5" />
                      Add child
                    </button>
                  }
                >
                  {children.length === 0 ? (
                    <p className="text-sm text-muted-foreground">
                      No children recorded — no child relief is claimed.
                    </p>
                  ) : (
                    children.map((child, index) => (
                      <RepeaterRow
                        key={index}
                        removeLabel="Remove this child"
                        onRemove={() => setChildren(children.filter((_, i) => i !== index))}
                      >
                        <Field label="Age">
                          <Num
                            value={child.age}
                            max={100}
                            onChange={(v) => patchChild(index, { age: v })}
                          />
                        </Field>
                        <Field label="Ability">
                          <Picker
                            value={child.abilityStatus}
                            onChange={(v) =>
                              patchChild(index, { abilityStatus: v ?? "NORMAL" })
                            }
                            options={CHILD_ABILITY.map((a) => ({
                              value: a,
                              label: CHILD_ABILITY_LABELS[a],
                            }))}
                          />
                        </Field>
                        <Field label="Currently studying">
                          <Picker
                            value={child.currentlyStudying}
                            onChange={(v) =>
                              patchChild(index, { currentlyStudying: v ?? "NONE" })
                            }
                            options={CHILD_STUDYING.map((c) => ({
                              value: c,
                              label: CHILD_STUDYING_LABELS[c],
                            }))}
                          />
                        </Field>
                        <Field label="Relief claimed here">
                          <Picker
                            value={child.pcbDeduction}
                            onChange={(v) => patchChild(index, { pcbDeduction: v ?? "FULL" })}
                            options={CHILD_DEDUCTION.map((d) => ({
                              value: d,
                              label: CHILD_DEDUCTION_LABELS[d],
                            }))}
                          />
                        </Field>
                      </RepeaterRow>
                    ))
                  )}
                </Stack>
              </>
            ) : section === "employment" ? (
              <>
                <Group title="Compensation" hint="The salary structure payroll calculates from.">
                  <Field label="Basis">
                    <Picker
                      value={profile.salaryType}
                      onChange={(v) => set("salaryType", v ?? "MONTHLY")}
                      options={SALARY_TYPES.map((t) => ({
                        value: t,
                        label: t === "MONTHLY" ? "Monthly" : "Hourly",
                      }))}
                    />
                  </Field>
                  {/* Only the field that matches the basis, so there's never a
                      monthly salary AND an hourly rate on record disagreeing. */}
                  {profile.salaryType === "MONTHLY" ? (
                    <Field label="Monthly salary">
                      <Money
                        value={profile.monthlySalary}
                        onChange={(v) => set("monthlySalary", v)}
                      />
                    </Field>
                  ) : (
                    <Field label="Hourly rate">
                      <Money value={profile.hourlyRate} onChange={(v) => set("hourlyRate", v)} />
                    </Field>
                  )}
                </Group>

                <Stack
                  title="Fixed adjustments"
                  hint="Recurring monthly additions or deductions added to every payroll run. One-off amounts belong on the run itself."
                  action={
                    <div className="flex items-center gap-2">
                      <button
                        type="button"
                        onClick={() => addAdjustment("ALLOWANCE")}
                        className="inline-flex h-9 items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-xs font-bold text-foreground transition hover:border-primary hover:text-primary"
                      >
                        <Plus className="h-3.5 w-3.5" />
                        Add allowance
                      </button>
                      <button
                        type="button"
                        onClick={() => addAdjustment("DEDUCTION")}
                        className="inline-flex h-9 items-center gap-1.5 rounded-xl border border-border bg-card px-3 text-xs font-bold text-foreground transition hover:border-primary hover:text-primary"
                      >
                        <Plus className="h-3.5 w-3.5" />
                        Add deduction
                      </button>
                    </div>
                  }
                >
                  {allowances.length === 0 ? (
                    <p className="text-sm text-muted-foreground">
                      No fixed adjustments. Add one for recurring monthly allowances, benefits, or
                      deductions.
                    </p>
                  ) : (
                    allowances.map((row, index) => {
                      const kind = kindOf(row.category);
                      return (
                        <RepeaterRow
                          key={index}
                          removeLabel="Remove this adjustment"
                          onRemove={() => setAllowances(allowances.filter((_, i) => i !== index))}
                        >
                          <Field label={kind === "DEDUCTION" ? "Deduction" : "Allowance"} span>
                            <Picker
                              value={row.category}
                              onChange={(v) =>
                                patchAllowance(index, {
                                  // Follow the category with the label, unless the
                                  // admin has renamed the row themselves.
                                  category: v ?? DEFAULT_CATEGORY[kind],
                                  name:
                                    row.name === labelForCategory(row.category) || !row.name
                                      ? labelForCategory(v ?? DEFAULT_CATEGORY[kind])
                                      : row.name,
                                })
                              }
                              options={categoriesFor(kind).map((c) => ({
                                value: c.code,
                                label: c.label,
                              }))}
                            />
                          </Field>
                          <Field label="Shown on the payslip as">
                            <Text
                              value={row.name}
                              onChange={(v) => patchAllowance(index, { name: v ?? "" })}
                            />
                          </Field>
                          <Field
                            label="Amount"
                            hint={
                              kind === "DEDUCTION" ? "Taken off every run." : "Added to every run."
                            }
                          >
                            <Money
                              value={row.amount}
                              onChange={(v) => patchAllowance(index, { amount: v })}
                            />
                          </Field>
                        </RepeaterRow>
                      );
                    })
                  )}
                </Stack>

                <Group
                  title="Employment dates"
                  hint="The join date pro-rates a partial first month. An end date belongs to Archive, below."
                  columns={1}
                >
                  <Field label="Join date">
                    <Text
                      type="date"
                      value={placement.joinDate}
                      onChange={(v) => {
                        // Written to the membership (which pro-rates accrual) AND
                        // to the profile copy, so the two can't disagree.
                        setPlacement((p) => ({ ...p, joinDate: v ?? "" }));
                        set("joinDate", v);
                      }}
                    />
                  </Field>
                </Group>

                <Group
                  title="Previous employment (TP3)"
                  hint="For a mid-year joiner: what a previous employer already paid and deducted, so PCB isn't calculated as if this is their only income."
                >
                  <Field label="Year">
                    <Text
                      value={profile.prevEmploymentYear === null ? null : String(profile.prevEmploymentYear)}
                      onChange={(v) => set("prevEmploymentYear", v === null ? null : Number(v))}
                      placeholder={String(new Date().getFullYear())}
                    />
                  </Field>
                  <Field label="Remuneration">
                    <Money
                      value={profile.prevRemuneration}
                      onChange={(v) => set("prevRemuneration", v)}
                    />
                  </Field>
                  <Field label="EPF paid">
                    <Money value={profile.prevEpf} onChange={(v) => set("prevEpf", v)} />
                  </Field>
                  <Field label="PCB paid">
                    <Money value={profile.prevPcb} onChange={(v) => set("prevPcb", v)} />
                  </Field>
                  <Field label="Allowable deductions">
                    <Money
                      value={profile.prevAllowableDeductions}
                      onChange={(v) => set("prevAllowableDeductions", v)}
                    />
                  </Field>
                  <Field label="Zakat paid">
                    <Money value={profile.prevZakat} onChange={(v) => set("prevZakat", v)} />
                  </Field>
                  <Field label="Includes an earlier spell here" span>
                    <Toggle
                      label="Covers a prior period at this company"
                      hint="Tick when the figures above include time they worked here before."
                      checked={profile.prevIncludesPriorThisOrgPeriod}
                      onChange={(v) => set("prevIncludesPriorThisOrgPeriod", v)}
                    />
                  </Field>
                </Group>

                <Group
                  title="Archive"
                  hint="Ending someone's employment happens here — one place, so a leave date and an archive flag can't disagree."
                >
                  <Field label="Last day" hint="Their final day of work. Pro-rates the last payroll run.">
                    <Text
                      type="date"
                      value={profile.leaveDate}
                      onChange={(v) => set("leaveDate", v)}
                    />
                  </Field>
                  <Field label="Archived" span>
                    <Toggle
                      label="Archive this employee"
                      hint="Keeps their history and payslips, but leaves them out of new payroll runs."
                      checked={profile.isArchived}
                      onChange={(v) => set("isArchived", v)}
                    />
                  </Field>
                  {profile.isArchived ? (
                    <Field label="Reason" hint="Why they were archived — resignation, end of contract." span>
                      <Text
                        value={profile.archiveReason}
                        onChange={(v) => set("archiveReason", v)}
                      />
                    </Field>
                  ) : null}
                </Group>
              </>
            ) : section === "statutory" ? (
              <>
                <Group title="EPF">
                  <Field label="EPF number" span>
                    <Text value={profile.epfNumber} onChange={(v) => set("epfNumber", v)} />
                  </Field>
                  <Field
                    label="Employee rate"
                    hint="Statutory is 11%. Entered as a percentage."
                  >
                    <Percent
                      value={profile.epfEmployeeRate}
                      onChange={(v) => set("epfEmployeeRate", v)}
                    />
                  </Field>
                  <Field label="Employee voluntary" hint="On top of the statutory rate.">
                    <Percent
                      value={profile.epfEmployeeVoluntary}
                      onChange={(v) => set("epfEmployeeVoluntary", v)}
                    />
                  </Field>
                  <Field label="Employer voluntary">
                    <Percent
                      value={profile.epfEmployerVoluntary}
                      onChange={(v) => set("epfEmployerVoluntary", v)}
                    />
                  </Field>
                  <Field label="Contributing" span>
                    <Toggle
                      label="Contributes to EPF"
                      hint="Off stops both employee and employer contributions."
                      checked={profile.contributeToEpf}
                      onChange={(v) => set("contributeToEpf", v)}
                    />
                  </Field>
                </Group>

                <Group title="SOCSO, EIS & SKBBK">
                  <Field label="SOCSO number">
                    <Text value={profile.socsoNumber} onChange={(v) => set("socsoNumber", v)} />
                  </Field>
                  <Field label="Scheme">
                    <Picker
                      value={profile.socsoScheme}
                      onChange={(v) => set("socsoScheme", v)}
                      allowNone
                      placeholder="Not set"
                      options={SOCSO_SCHEMES.map((s) => ({
                        value: s,
                        label: SOCSO_SCHEME_LABELS[s],
                      }))}
                    />
                  </Field>
                  <Toggle
                    label="Contributes to EIS"
                    checked={profile.contributeToEis}
                    onChange={(v) => set("contributeToEis", v)}
                  />
                  <Toggle
                    label="Contributes to SKBBK"
                    checked={profile.contributeToSkbbk}
                    onChange={(v) => set("contributeToSkbbk", v)}
                  />
                </Group>

                <Group title="Income tax">
                  <Field label="Income tax number">
                    <Text
                      value={profile.incomeTaxNumber}
                      onChange={(v) => set("incomeTaxNumber", v)}
                    />
                  </Field>
                  <Field label="SSFW number" hint="Foreign-worker social security, where it applies.">
                    <Text value={profile.ssfwNumber} onChange={(v) => set("ssfwNumber", v)} />
                  </Field>
                  <Toggle
                    label="PCB borne by employer"
                    hint="The company pays the tax instead of deducting it."
                    checked={profile.pcbBorneByEmployer}
                    onChange={(v) => set("pcbBorneByEmployer", v)}
                  />
                  <Toggle
                    label="Reported to LHDN"
                    checked={profile.reportedToLhdn}
                    onChange={(v) => set("reportedToLhdn", v)}
                  />
                </Group>

                <Group title="Bank / payout" hint="Where the money goes once payroll has run.">
                  <Field label="Method" span>
                    <Picker
                      value={profile.paymentMethod}
                      onChange={(v) => set("paymentMethod", v ?? "BANK_TRANSFER")}
                      options={PAYMENT_METHODS.map((m) => ({
                        value: m,
                        label: PAYMENT_METHOD_LABELS[m],
                      }))}
                    />
                  </Field>
                  {/* Bank details are only meaningful for a transfer. */}
                  {profile.paymentMethod === "BANK_TRANSFER" ? (
                    <>
                      <Field label="Bank">
                        <Text value={profile.bankName} onChange={(v) => set("bankName", v)} />
                      </Field>
                      <Field label="Account number">
                        <Text
                          value={profile.bankAccountNumber}
                          onChange={(v) => set("bankAccountNumber", v)}
                        />
                      </Field>
                      <Field
                        label="Account holder"
                        hint="As printed by the bank — a mismatch bounces the transfer."
                        span
                      >
                        <Text
                          value={profile.bankAccountHolderName}
                          onChange={(v) => set("bankAccountHolderName", v)}
                        />
                      </Field>
                    </>
                  ) : null}
                </Group>
              </>
            ) : (
              <>
                <Group title="Identity at work" hint="What this person is called and known by across the app.">
                  <Field label="Full name">
                    <Text
                      value={placement.name}
                      onChange={(v) => setPlacement((p) => ({ ...p, name: v ?? "" }))}
                    />
                  </Field>
                  <Field label="Employee number">
                    <Text
                      value={placement.employeeNumber}
                      onChange={(v) => setPlacement((p) => ({ ...p, employeeNumber: v ?? "" }))}
                      placeholder="EMP-001"
                    />
                  </Field>
                  <Field label="Job title" span>
                    <Text
                      value={placement.jobTitle}
                      onChange={(v) => setPlacement((p) => ({ ...p, jobTitle: v ?? "" }))}
                    />
                  </Field>
                </Group>

                <Group
                  title="Placement"
                  hint="What the rest of the app routes on: approvals follow the supervisor, entitlements follow the policy."
                >
                  <Field label="Role">
                    <Picker
                      value={placement.role}
                      onChange={(v) => setPlacement((p) => ({ ...p, role: v ?? p.role }))}
                      // Employee ⇄ Supervisor only. Granting administrative
                      // access is a different decision on a different screen,
                      // and doing it here would quietly put an admin into an
                      // approval chain they're meant to sit outside of.
                      options={STAFF_ROLES.map((r) => ({ value: r, label: r }))}
                    />
                  </Field>
                  <Field label="Approving supervisor">
                    <Picker
                      value={placement.supervisorId}
                      onChange={(v) =>
                        setPlacement((p) => ({ ...p, supervisorId: v ?? NONE }))
                      }
                      placeholder="None"
                      options={[
                        { value: NONE, label: "None" },
                        ...supervisorOptions.map((e) => ({
                          value: e.id,
                          label: e.name?.trim() ? `${e.name} — ${e.email}` : e.email,
                        })),
                      ]}
                    />
                  </Field>
                  <Field label="Policy">
                    <Picker
                      value={placement.policyId}
                      onChange={(v) => setPlacement((p) => ({ ...p, policyId: v ?? NONE }))}
                      placeholder="Default"
                      options={[
                        { value: NONE, label: "Default policy" },
                        ...activePolicies.map((p) => ({ value: p.id, label: p.name })),
                      ]}
                    />
                  </Field>
                  <Field label="Department">
                    <Text value={profile.department} onChange={(v) => set("department", v)} />
                  </Field>
                </Group>

                <Group title="Working arrangement">
                  <Field label="Location">
                    <Text value={profile.location} onChange={(v) => set("location", v)} />
                  </Field>
                  <Field label="Work schedule">
                    <Text value={profile.workSchedule} onChange={(v) => set("workSchedule", v)} />
                  </Field>
                  <Field
                    label="Temporary review date"
                    hint="Probation end or fixed-term checkpoint. Only meaningful on a temporary policy."
                    span
                  >
                    <Text
                      type="date"
                      value={profile.temporaryReviewDate}
                      onChange={(v) => set("temporaryReviewDate", v)}
                    />
                  </Field>
                </Group>

                <Group title="Payroll handling" hint="How this person's pay run is labelled and grouped.">
                  <Field label="Payroll policy">
                    <Text
                      value={profile.payrollPolicy}
                      onChange={(v) => set("payrollPolicy", v)}
                    />
                  </Field>
                  <Field label="Payroll cycle" hint="e.g. Monthly, Bi-weekly.">
                    <Text value={profile.payrollCycle} onChange={(v) => set("payrollCycle", v)} />
                  </Field>
                </Group>
              </>
            )}
          </div>
        </div>
      )}

      {/* Appears only once something has changed — so "did that save?" is never
          a question, and an untouched record shows no call to action. */}
      {dirty ? (
        <div className="fixed inset-x-0 bottom-0 z-40 border-t border-border/70 bg-card/95 px-4 py-3 backdrop-blur-xl">
          <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-3">
            <p className="text-sm font-semibold text-foreground">Unsaved changes</p>
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={discard}
                disabled={saving}
                className="inline-flex h-11 items-center gap-1.5 rounded-full border border-border bg-card px-4 text-sm font-bold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
              >
                <RotateCcw className="h-3.5 w-3.5" />
                Discard
              </button>
              <button
                type="button"
                onClick={() => void handleSave()}
                disabled={saving}
                className="inline-flex h-11 items-center gap-2 rounded-full bg-primary px-5 text-sm font-bold text-primary-foreground shadow-sm transition hover:opacity-90 disabled:opacity-60"
              >
                {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                Save changes
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
