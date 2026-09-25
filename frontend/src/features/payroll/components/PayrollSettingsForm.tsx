import { useEffect, useRef, useState } from "react";
import { CircleAlert } from "lucide-react";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import * as cache from "@/shared/lib/api-cache";
import {
  getPayrollCompanyInfo,
  getPortalCredentials,
  getPayrollSettings,
  savePayrollCompanyInfo,
  savePayrollSettings,
  workingDaysRuleLabels,
  type PayrollCompanyInfo,
  emptyXeroMapping,
  type IdType,
  type PayrollSettings,
  type PayrollXeroMapping,
  type WorkingDaysRule,
} from "../api";
import {
  CARD,
  ERROR_PANEL,
  HINT,
  INPUT,
  LABEL,
  NOTE_PANEL,
} from "../lib/ui";
import { getXeroStatus } from "@/features/settings/api";
import { Skeleton, SkeletonPanels } from "@/shared/components/Skeleton";
import { UnsavedChangesBar } from "@/shared/components/UnsavedChangesBar";
import { CheckBox } from "./PayrollCheckbox";
import { DISBURSEMENT_BANKS } from "../lib/disbursement";
import {
  CP8D_FURNISH_TYPE_OPTIONS,
  EMPLOYER_CATEGORY_OPTIONS,
  EMPLOYER_STATUS_OPTIONS,
  REFERENCE_TYPE_OPTIONS,
} from "../lib/company-info-options";
import { PayrollSelect } from "./PayrollSelect";
import { PortalCredentialsSection } from "./settings/PortalCredentialsSection";
import { XeroSyncSection } from "./settings/XeroSyncSection";

// How payroll runs, and who the employer is for filing purposes.
//
// Two cards, one save. They are two halves of "set payroll up" and a save
// button per card reads as though pressing one discarded the other's edits.
//
// The operational rules and the filing identity are deliberately separate
// concepts — one changes when the org changes how it pays, the other only
// when its registrations change — but an admin setting payroll up for the
// first time fills both in one sitting.
// LHDN's own identification categories for a declarant.
const ID_TYPES: IdType[] = ["NRIC", "PASSPORT", "ARMY", "POLICE"];

type Snapshot = {
  settings: PayrollSettings;
  info: PayrollCompanyInfo;
  xeroMapping: PayrollXeroMapping;
};

// The dirty-check key: everything save posts, serialised in one fixed shape.
function snapshot(
  settings: PayrollSettings,
  info: PayrollCompanyInfo,
  xeroMapping: PayrollXeroMapping,
): string {
  return JSON.stringify({ settings, info, xeroMapping } satisfies Snapshot);
}

// The Form E summary line — the red tab's explanation, in view on the tab.
const FIELDS_MISSING =
  "flex items-start gap-2 rounded-2xl border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm font-medium text-destructive";

const ID_TYPE_LABELS: Record<IdType, string> = {
  NRIC: "NRIC",
  PASSPORT: "Passport",
  ARMY: "Army number",
  POLICE: "Police number",
};

export function PayrollSettingsForm() {
  const [section, setSection] = useState<SettingsSection>("general");
  // Seeded straight from the cache so a revisit renders the form on the first
  // frame. These are a WORKING COPY — patchSettings/patchInfo edit them and
  // save posts them — so they are deliberately not bound to the query: a
  // background revalidate landing mid-edit must not overwrite what is being
  // typed. cache.peek is a plain read, safe in an initializer.
  const [settings, setSettings] = useState<PayrollSettings | null>(
    () => cache.peek<PayrollSettings>("/payroll/settings")?.data ?? null,
  );
  const [info, setInfo] = useState<PayrollCompanyInfo | null>(
    () => cache.peek<PayrollCompanyInfo>("/payroll/company-info")?.data ?? null,
  );
  // Held decoded. The backend stores it as a JSON blob on the settings row,
  // so it is parsed on load and re-serialised on save rather than being
  // edited as text.
  const [xeroMapping, setXeroMapping] = useState<PayrollXeroMapping>(() => {
    const cached = cache.peek<PayrollSettings>("/payroll/settings")?.data;
    return cached ? parseMapping(cached.xeroMappingJson) : emptyXeroMapping();
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // A refused save, shown on the save bar next to the button — kept apart
  // from `error`, which is a load failure and blanks the page.
  const [saveError, setSaveError] = useState<string | null>(null);

  // What was last loaded or saved — the working copy above is compared with
  // it to decide whether there is anything to save, and Discard goes back to
  // it. One serialised snapshot rather than a per-field dirty map, the same
  // approach the employee profile uses, so it cannot drift as fields are added.
  const [baseline, setBaseline] = useState<string | null>(() => {
    const s = cache.peek<PayrollSettings>("/payroll/settings")?.data;
    const i = cache.peek<PayrollCompanyInfo>("/payroll/company-info")?.data;
    return s && i ? snapshot(s, i, parseMapping(s.xeroMappingJson)) : null;
  });
  // How many portals have a password on file — only the pill's subtitle needs
  // it. Read through the SAME cache key the credentials section uses, so the
  // count and the section it summarises can't disagree and the page doesn't
  // fetch the list twice.
  const credentialsQuery = useCachedQuery("/payroll/portal-credentials", getPortalCredentials);
  const credentialCount = (credentialsQuery.data ?? []).filter((row) => row.hasPassword).length;
  const countCredentials = credentialsQuery.refresh;

  // The reference hides the Xero pieces entirely until Xero is connected,
  // rather than offering a mapping that could never post. Cached on the key
  // the connection card already uses.
  const xeroStatusQuery = useCachedQuery("/xero/status", getXeroStatus);
  const xeroConnected = xeroStatusQuery.data?.connected ?? false;

  // The whole page used to blank itself to a skeleton on every visit, because
  // `loading` started true and nothing was cached. Both reads go through the
  // cache now; the working copy above is already populated on a revisit, and
  // this only fills it on the first ever load.
  const settingsQuery = useCachedQuery("/payroll/settings", getPayrollSettings);
  const infoQuery = useCachedQuery("/payroll/company-info", getPayrollCompanyInfo);
  const loading = (settingsQuery.loading || infoQuery.loading) && (!settings || !info);

  const seeded = useRef(settings !== null && info !== null);
  useEffect(() => {
    if (seeded.current) return;
    const nextSettings = settingsQuery.data;
    const nextInfo = infoQuery.data;
    if (!nextSettings || !nextInfo) return;
    seeded.current = true;
    const nextMapping = parseMapping(nextSettings.xeroMappingJson);
    setSettings(nextSettings);
    setInfo(nextInfo);
    setXeroMapping(nextMapping);
    setBaseline(snapshot(nextSettings, nextInfo, nextMapping));
  }, [settingsQuery.data, infoQuery.data]);

  useEffect(() => {
    const first = settingsQuery.error ?? infoQuery.error;
    if (first) setError(first);
  }, [settingsQuery.error, infoQuery.error]);

  // The Xero pill is about to disappear, so don't strand the admin on a
  // section that no longer has one.
  useEffect(() => {
    if (xeroStatusQuery.data && !xeroStatusQuery.data.connected) {
      setSection((current) => (current === "xero" ? "general" : current));
    }
  }, [xeroStatusQuery.data]);

  function patchSettings(patch: Partial<PayrollSettings>) {
    setSettings((current) => (current ? { ...current, ...patch } : current));
  }

  function patchInfo(patch: Partial<PayrollCompanyInfo>) {
    setInfo((current) => (current ? { ...current, ...patch } : current));
  }

  const dirty =
    settings !== null &&
    info !== null &&
    baseline !== null &&
    snapshot(settings, info, xeroMapping) !== baseline;

  function discard() {
    if (!baseline) return;
    const restored = JSON.parse(baseline) as Snapshot;
    setSettings(restored.settings);
    setInfo(restored.info);
    setXeroMapping(restored.xeroMapping);
    setSaveError(null);
  }

  // Enter in any field still saves, as it always has.
  function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    void save();
  }

  async function save() {
    if (!settings || !info) return;

    setSaving(true);
    setSaveError(null);

    try {
      const {
        isConfigured: _configured,
        updatedAt: _updated,
        ...settingsBody
      } = settings;
      const {
        isConfigured: _infoConfigured,
        updatedAt: _infoUpdated,
        ...infoBody
      } = info;

      const [nextSettings, nextInfo] = await Promise.all([
        savePayrollSettings({
          ...settingsBody,
          xeroMappingJson: JSON.stringify(xeroMapping),
        }),
        savePayrollCompanyInfo(infoBody),
      ]);

      const nextMapping = parseMapping(nextSettings.xeroMappingJson);
      setSettings(nextSettings);
      setInfo(nextInfo);
      setXeroMapping(nextMapping);
      setBaseline(snapshot(nextSettings, nextInfo, nextMapping));
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : "Could not save the payroll settings.");
    } finally {
      setSaving(false);
    }
  }

  // Completeness, in the reference's vocabulary. "Complete" means the
  // fields that actually block something downstream are filled — not that
  // every optional box is.
  //
  // The Form E check is the SAME four fields PayrollRunReadiness refuses a
  // submission over, so "tab is red" and "submit is blocked" cannot disagree.
  const formEMissing = [
    info?.employerName,
    info?.employerTin,
    info?.registrationNo,
    info?.perkesoEmployerCode,
  ].filter((v) => !v?.trim()).length;
  const formEComplete = formEMissing === 0;

  const neverSaved = settings !== null && !settings.isConfigured;

  const status: Record<SettingsSection, SectionStatus> = {
    // Nothing on this tab can be missing — every field ships with its
    // statutory default. It is incomplete only until the defaults have been
    // saved once, so it says that rather than "required fields missing".
    general: {
      complete: Boolean(settings?.isConfigured),
      pendingLabel: "Not saved",
      pendingTitle: "Nothing is missing — review the defaults and save",
    },
    formE: { complete: formEComplete },
    credentials: {
      complete: credentialCount > 0,
      optional: true,
      savedLabel: `${credentialCount} saved`,
    },
    // Optional while the org has not opted into posting on approval —
    // nothing is blocked by an unmapped account it will never use. Once the
    // switch is on, an unmapped salary account DOES stop the journal, so it
    // becomes a required section and earns the red.
    xero: settings?.syncPayrollToXeroOnSubmit
      ? { complete: Boolean(xeroMapping.accounts.salary) }
      : { complete: false, optional: true },
  };

  if (loading) {
    return (
      <div className="space-y-6">
        <div className="space-y-2">
          <Skeleton className="h-6 w-40" />
          <Skeleton className="h-3 w-64" />
        </div>
        <div className="flex flex-wrap gap-2">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} className="h-8 w-28 rounded-full" />
          ))}
        </div>
        <SkeletonPanels count={2} />
      </div>
    );
  }
  if (error && !settings) return <section className={ERROR_PANEL}>Error: {error}</section>;
  if (!settings || !info) return null;

  return (
    <form onSubmit={handleSubmit} className="space-y-6 pb-24">
      {!settings.isConfigured ? (
        <section className={NOTE_PANEL}>
          Payroll has not been set up for this organisation yet. Nothing is missing on the
          General tab — its values are the Malaysian statutory defaults. Review them and press
          Save payroll settings to confirm.
        </section>
      ) : null}

      <SectionPicker
        value={section}
        onChange={setSection}
        status={status}
        xeroConnected={xeroConnected}
      />

      {section === "general" ? (
        <>
          {/* ── Working-days rule ──────────────────────────────────── */}
          <section className={CARD}>
            <header className="mb-5">
              <h3 className="text-[15px] font-semibold text-foreground">Working-days rule</h3>
              <p className={HINT}>
                Used to convert a monthly salary into the daily and hourly rate for
                overtime. It does <strong>not</strong> govern proration for a mid-month
                joiner or leaver — Employment Act s.18A fixes that to calendar days
                whatever is chosen here.
              </p>
            </header>

            <div className="grid gap-5 sm:grid-cols-2">
              <div>
                <label className={LABEL} htmlFor="workingDaysRule">
                  Days-per-month basis
                </label>
                <PayrollSelect
                  id="workingDaysRule"
                  value={settings.workingDaysRule}
                  onChange={(next) =>
                    next && patchSettings({ workingDaysRule: next as WorkingDaysRule })
                  }
                  options={(Object.keys(workingDaysRuleLabels) as WorkingDaysRule[]).map(
                    (rule) => ({ value: rule, label: workingDaysRuleLabels[rule] }),
                  )}
                />
              </div>
            </div>
          </section>

          {/* ── EPF defaults ───────────────────────────────────────── */}
          <section className={CARD}>
            <header className="mb-5">
              <h3 className="text-[15px] font-semibold text-foreground">EPF defaults</h3>
              <p className={HINT}>
                Both rates are set by EPF Act 452 (Third Schedule) and are not editable.
                Employee 11% is the statutory minimum under Part A; the employer rate
                steps automatically — 13% at or below RM 5,000, 12% above it.
                Above-statutory employee contributions are set per person on their
                profile.
              </p>
            </header>

            <div className="grid gap-5 sm:grid-cols-2">
              {/* Read-only rather than a disabled input: a greyed-out box still
                  reads as something you could switch on. */}
              <Locked
                label="Employee rate (%)"
                value="11"
                note="Statutory minimum, EPF Act 452 Third Schedule Part A."
              />
              <Locked
                label="Employer rate (%)"
                value="13 / 12"
                note="Stepped on the wage: 13% up to RM 5,000, 12% above."
              />
              <Field
                id="epfEmployerNo"
                label="EPF employer no. (KWSP)"
                value={info.epfEmployerNo}
                onChange={(epfEmployerNo) => patchInfo({ epfEmployerNo })}
                placeholder="12345678"
              />
            </div>
          </section>

          {/* ── HRD Corp levy ──────────────────────────────────────── */}
          <section className={CARD}>
            <header className="mb-5">
              <h3 className="text-[15px] font-semibold text-foreground">HRD Corp levy</h3>
              <p className={HINT}>
                1% of wages under Part I, 0.5% under Part II. Only Malaysian citizens
                count towards it.
              </p>
            </header>

            <div className="grid gap-5 sm:grid-cols-2">
              <Toggle
                id="hrdfEnabled"
                label="This organisation pays the HRD Corp levy"
                hint="Turning it off clears the rate, so a stale figure cannot come back later."
                checked={settings.hrdfEnabled}
                onChange={(hrdfEnabled) =>
                  patchSettings({ hrdfEnabled, hrdfRate: hrdfEnabled ? settings.hrdfRate : null })
                }
              />
              <div>
                <label className={LABEL} htmlFor="hrdfRate">
                  Levy rate (%)
                </label>
                <input
                  id="hrdfRate"
                  type="number"
                  step="0.1"
                  min="0"
                  max="10"
                  className={INPUT}
                  disabled={!settings.hrdfEnabled}
                  value={settings.hrdfRate ?? ""}
                  onChange={(e) =>
                    patchSettings({
                      hrdfRate: e.target.value === "" ? null : Number(e.target.value),
                    })
                  }
                />
              </div>
              <Field
                id="hrdfEmployerNo"
                label="HRD Corp employer no."
                value={info.hrdfEmployerNo}
                onChange={(hrdfEmployerNo) => patchInfo({ hrdfEmployerNo })}
                placeholder="Leave blank if not registered"
              />
            </div>
          </section>

          {/* ── PCB compliance ─────────────────────────────────────── */}
          <section className={CARD}>
            <header className="mb-5">
              <h3 className="text-[15px] font-semibold text-foreground">PCB compliance</h3>
              <p className={HINT}>
                Optional reliefs applied automatically in the monthly PCB. Strictly, per
                LHDN MTD Spec 2026, these are TP1 items the employee declares — but most
                payroll systems apply them because the employer already knows the exact
                figures. Turn it off to leave them to the year-end Form BE.
              </p>
            </header>

            <Toggle
              id="autoApplySocsoEisRelief"
              label="Auto-apply the RM 350 SOCSO + EIS relief"
              checked={settings.autoApplySocsoEisRelief}
              onChange={(autoApplySocsoEisRelief) =>
                patchSettings({ autoApplySocsoEisRelief })
              }
            />
          </section>

          {/* ── SKBBK, read-only ───────────────────────────────────── */}
          <section className={CARD}>
            <header className="mb-3">
              <h3 className="text-[15px] font-semibold text-foreground">
                SKBBK — Skim LINDUNG 24 Jam
              </h3>
              <p className={HINT}>
                Nothing to configure. PERKESO gazettes the rate per rollout phase and the
                engine reads the table, so there is no setting an admin could get wrong.
              </p>
            </header>

            <dl className="grid gap-3 sm:grid-cols-2">
              <Fact label="In force from" value="1 June 2026" />
              <Fact label="Employee share" value="0.75% of wages, employee only" />
            </dl>

            <p className={`${HINT} mt-3`}>
              A run for a period before June 2026 deducts nothing, so re-running an older
              month reproduces what it was actually filed under.
            </p>
          </section>

          {/* ── Disbursement bank ──────────────────────────────────── */}
          <section className={CARD}>
            <header className="mb-5">
              <h3 className="text-[15px] font-semibold text-foreground">
                Payroll disbursement bank
              </h3>
              <p className={HINT}>
                The company account salaries are paid from. This decides which bulk-upload
                file a run produces. It does not restrict where employees bank — every
                format pays out to any Malaysian bank.
              </p>
            </header>

            <div className="grid gap-5 sm:grid-cols-2">
              <div>
                <label className={LABEL} htmlFor="payrollBankName">
                  Bank
                </label>
                <PayrollSelect
                  id="payrollBankName"
                  value={settings.payrollBankName}
                  emptyLabel="Select a bank"
                  placeholder="Select a bank"
                  onChange={(payrollBankName) => patchSettings({ payrollBankName })}
                  options={DISBURSEMENT_BANKS}
                />
                <p className={HINT}>
                  Until this is set, a run produces no upload file — the layouts are not
                  interchangeable, so there is nothing safe to default to. Choosing
                  &ldquo;Other&rdquo; is fine: key the payments in from the Payment
                  Schedule report instead.
                </p>
              </div>

              <Field
                id="ecpPayorAccountNo"
                label="Account number"
                value={settings.ecpPayorAccountNo}
                onChange={(ecpPayorAccountNo) => patchSettings({ ecpPayorAccountNo })}
                placeholder="Company payroll account no."
                hint="The account salaries are debited from. Public Bank needs exactly 10 digits — the file is refused otherwise rather than rejected later by the portal."
              />
              <Field
                id="payorAccountHolderName"
                label="Account holder name"
                value={settings.payorAccountHolderName}
                onChange={(payorAccountHolderName) => patchSettings({ payorAccountHolderName })}
                placeholder="As registered with the bank"
              />
              <Field
                id="payorOrganisationCode"
                label="Organisation code"
                value={settings.payorOrganisationCode}
                onChange={(payorOrganisationCode) => patchSettings({ payorOrganisationCode })}
                placeholder="Issued by your bank"
                hint="Required by Maybank (Corporate ID) and CIMB (Autopay Organisation Code). Public Bank and Hong Leong do not use it."
              />
            </div>
          </section>

          {/* ── Xero sync on submit ────────────────────────────────── */}
          {xeroConnected ? (
            <section className={CARD}>
              <header className="mb-5">
                <h3 className="text-[15px] font-semibold text-foreground">
                  Xero sync on submit
                </h3>
                <p className={HINT}>
                  When a run is approved, post the payroll summary as a manual journal.
                  Best effort: if Xero is unreachable the approval still stands and the
                  error is recorded on the run.
                </p>
              </header>

              <Toggle
                id="syncPayrollToXeroOnSubmit"
                label="Post the payroll journal to Xero when a run is approved"
                checked={settings.syncPayrollToXeroOnSubmit}
                onChange={(syncPayrollToXeroOnSubmit) =>
                  patchSettings({ syncPayrollToXeroOnSubmit })
                }
              />
            </section>
          ) : null}
        </>
      ) : null}


      {section === "formE" ? (
        <>
        {/* Says how many, so the red tab and the red boxes below add up. */}
        {formEMissing > 0 ? (
          <p role="status" className={FIELDS_MISSING}>
            <CircleAlert className="size-4 shrink-0" aria-hidden />
            {formEMissing === 1
              ? "1 required field below is empty (marked *). Payroll can't be sent for approval until it's filled."
              : `${formEMissing} required fields below are empty (marked *). Payroll can't be sent for approval until they're filled.`}
          </p>
        ) : null}
        <section className={CARD}>
          <header className="mb-5">
            <h3 className="text-[15px] font-semibold text-foreground">Employer details</h3>
            <p className={HINT}>
              What appears on every statutory submission. Each body issues its own registration
              number, so these are not interchangeable.
            </p>
          </header>

          <div className="grid gap-5 sm:grid-cols-2">
            <Field
              id="employerName"
              label="Employer name"
              value={info.employerName}
              onChange={(employerName) => patchInfo({ employerName })}
              placeholder="e.g. Globe Engineering Sdn Bhd"
              required
            />
            <Field
              id="employerTin"
              label="LHDN employer no. (E number)"
              value={info.employerTin}
              onChange={(employerTin) => patchInfo({ employerTin })}
              placeholder="e.g. E 1234567890"
              hint="Needed for the CP39 monthly file and the CP8D annual upload."
              required
            />
            <Field
              id="registrationNo"
              label="SSM registration no."
              value={info.registrationNo}
              onChange={(registrationNo) => patchInfo({ registrationNo })}
              placeholder="e.g. 202001012345"
              required
            />
            {/* The employer's own income-tax file (Form E). Carried over from
                the previous system, which asked for it here. */}
            <div>
              <label className={LABEL} htmlFor="referenceType">
                Income tax reference type
              </label>
              <PayrollSelect
                id="referenceType"
                value={info.referenceType}
                emptyLabel="Not set"
                onChange={(referenceType) => patchInfo({ referenceType })}
                options={REFERENCE_TYPE_OPTIONS}
              />
            </div>
            <Field
              id="referenceNo"
              label="Income tax reference no."
              value={info.referenceNo}
              onChange={(referenceNo) => patchInfo({ referenceNo })}
              placeholder="e.g. 1234567890"
            />
            <Field
              id="perkesoEmployerCode"
              label="PERKESO employer code"
              value={info.perkesoEmployerCode}
              onChange={(perkesoEmployerCode) => patchInfo({ perkesoEmployerCode })}
              placeholder="e.g. A1234567890"
              hint="Needed for the SOCSO / EIS submission file."
              required
            />
            <Field
              id="epfEmployerNo"
              label="KWSP employer no."
              value={info.epfEmployerNo}
              onChange={(epfEmployerNo) => patchInfo({ epfEmployerNo })}
              placeholder="e.g. 7654321"
            />
            <Field
              id="hrdfEmployerNo"
              label="HRD Corp employer no."
              value={info.hrdfEmployerNo}
              onChange={(hrdfEmployerNo) => patchInfo({ hrdfEmployerNo })}
              placeholder="Leave blank if not registered"
            />
            <Field
              id="email"
              label="Contact email"
              type="email"
              value={info.email}
              onChange={(email) => patchInfo({ email })}
            />
            <Field
              id="phone"
              label="Contact phone"
              value={info.phone}
              onChange={(phone) => patchInfo({ phone })}
            />
          </div>

          {/* How LHDN classifies the employer on Form E and CP8D. */}
          <div className="mt-5 grid gap-5 border-t border-border/60 pt-5 sm:grid-cols-2">
            <div>
              <label className={LABEL} htmlFor="employerCategory">
                Employer category
              </label>
              <PayrollSelect
                id="employerCategory"
                value={info.employerCategory}
                emptyLabel="Not set"
                onChange={(employerCategory) => patchInfo({ employerCategory })}
                options={EMPLOYER_CATEGORY_OPTIONS}
              />
            </div>
            <div>
              <label className={LABEL} htmlFor="employerStatus">
                Employer status
              </label>
              <PayrollSelect
                id="employerStatus"
                value={info.employerStatus}
                emptyLabel="Not set"
                onChange={(employerStatus) => patchInfo({ employerStatus })}
                options={EMPLOYER_STATUS_OPTIONS}
              />
            </div>
            <div>
              <label className={LABEL} htmlFor="cp8dFurnishType">
                CP8D furnish type
              </label>
              <PayrollSelect
                id="cp8dFurnishType"
                value={info.cp8dFurnishType}
                emptyLabel="Not set"
                onChange={(cp8dFurnishType) => patchInfo({ cp8dFurnishType })}
                options={CP8D_FURNISH_TYPE_OPTIONS}
              />
            </div>
          </div>

          <div className="mt-5 grid gap-5 border-t border-border/60 pt-5 sm:grid-cols-2">
            <Field
              id="addressLine1"
              label="Address line 1"
              value={info.addressLine1}
              onChange={(addressLine1) => patchInfo({ addressLine1 })}
            />
            <Field
              id="addressLine2"
              label="Address line 2"
              value={info.addressLine2}
              onChange={(addressLine2) => patchInfo({ addressLine2 })}
            />
            <Field
              id="postcode"
              label="Postcode"
              value={info.postcode}
              onChange={(postcode) => patchInfo({ postcode })}
            />
            <Field
              id="city"
              label="City"
              value={info.city}
              onChange={(city) => patchInfo({ city })}
            />
            <Field
              id="state"
              label="State"
              value={info.state}
              onChange={(state) => patchInfo({ state })}
            />
            <Field
              id="country"
              label="Country"
              value={info.country}
              onChange={(country) => patchInfo({ country })}
              placeholder="Malaysia"
            />
            <Field
              id="handphone"
              label="Mobile number"
              value={info.handphone}
              onChange={(handphone) => patchInfo({ handphone })}
            />
            <Field
              id="zakatNumber"
              label="Zakat registration no."
              value={info.zakatNumber}
              onChange={(zakatNumber) => patchInfo({ zakatNumber })}
              hint="For an employer remitting zakat on employees' behalf."
            />
          </div>
        </section>

        <section className={CARD}>
          <header className="mb-5">
            <h3 className="text-[15px] font-semibold text-foreground">
              Form E declarant and tax agent
            </h3>
            <p className={HINT}>
              Who signs the employer's annual return, and the agent who prepared it if one
              did. Stored against the filing — the generated Form E does not print them yet.
            </p>
          </header>

          <div className="grid gap-5 sm:grid-cols-2">
            <Field
              id="declarantName"
              label="Declarant name"
              value={info.declarantName}
              onChange={(declarantName) => patchInfo({ declarantName })}
            />
            <Field
              id="declarantPosition"
              label="Position"
              value={info.declarantPosition}
              onChange={(declarantPosition) => patchInfo({ declarantPosition })}
              placeholder="Director"
            />

            <div>
              <label className={LABEL} htmlFor="declarantIdType">
                Identification type
              </label>
              <PayrollSelect
                id="declarantIdType"
                value={info.declarantIdType}
                emptyLabel="Not set"
                onChange={(next) => patchInfo({ declarantIdType: next as IdType | null })}
                options={ID_TYPES.map((idType) => ({
                  value: idType,
                  label: ID_TYPE_LABELS[idType],
                }))}
              />
            </div>

            <Field
              id="declarantIdNumber"
              label="Identification no."
              value={info.declarantIdNumber}
              onChange={(declarantIdNumber) => patchInfo({ declarantIdNumber })}
            />

            <Field
              id="taxAgentName"
              label="Tax agent name"
              value={info.taxAgentName}
              onChange={(taxAgentName) => patchInfo({ taxAgentName })}
            />
            <Field
              id="taxAgentTin"
              label="Tax agent TIN"
              value={info.taxAgentTin}
              onChange={(taxAgentTin) => patchInfo({ taxAgentTin })}
            />
            <Field
              id="taxAgentLicenceNo"
              label="Tax agent licence no."
              value={info.taxAgentLicenceNo}
              onChange={(taxAgentLicenceNo) => patchInfo({ taxAgentLicenceNo })}
            />
            <Field
              id="taxAgentPhone"
              label="Tax agent phone"
              value={info.taxAgentPhone}
              onChange={(taxAgentPhone) => patchInfo({ taxAgentPhone })}
            />
            <Field
              id="taxAgentEmail"
              label="Tax agent email"
              type="email"
              value={info.taxAgentEmail}
              onChange={(taxAgentEmail) => patchInfo({ taxAgentEmail })}
            />
          </div>

          {/* The agent's firm — Form E asks for it apart from the agent. */}
          <div className="mt-5 grid gap-5 border-t border-border/60 pt-5 sm:grid-cols-2">
            <Field
              id="taxAgentFirmName"
              label="Tax agent firm name"
              value={info.taxAgentFirmName}
              onChange={(taxAgentFirmName) => patchInfo({ taxAgentFirmName })}
            />
            <Field
              id="taxAgentFirmAddressLine1"
              label="Firm address line 1"
              value={info.taxAgentFirmAddressLine1}
              onChange={(taxAgentFirmAddressLine1) => patchInfo({ taxAgentFirmAddressLine1 })}
            />
            <Field
              id="taxAgentFirmAddressLine2"
              label="Firm address line 2"
              value={info.taxAgentFirmAddressLine2}
              onChange={(taxAgentFirmAddressLine2) => patchInfo({ taxAgentFirmAddressLine2 })}
            />
            <Field
              id="taxAgentFirmPostcode"
              label="Firm postcode"
              value={info.taxAgentFirmPostcode}
              onChange={(taxAgentFirmPostcode) => patchInfo({ taxAgentFirmPostcode })}
            />
            <Field
              id="taxAgentFirmCity"
              label="Firm city"
              value={info.taxAgentFirmCity}
              onChange={(taxAgentFirmCity) => patchInfo({ taxAgentFirmCity })}
            />
            <Field
              id="taxAgentFirmState"
              label="Firm state"
              value={info.taxAgentFirmState}
              onChange={(taxAgentFirmState) => patchInfo({ taxAgentFirmState })}
            />
          </div>
        </section>
        </>
      ) : null}

      {/* Saves per portal, not with the rest — a password write is its own
          decision and should not ride along on a settings save.
          `countCredentials` is passed by identity rather than wrapped in an
          arrow: the section re-runs its load effect when this prop changes,
          and a fresh closure each render would make it refetch in a loop. */}
      {section === "credentials" ? (
        <PortalCredentialsSection onChanged={countCredentials} />
      ) : null}

      {section === "xero" && xeroConnected ? (
        <XeroSyncSection mapping={xeroMapping} onChange={setXeroMapping} />
      ) : null}


      {/* The save bar, as on the employee profile: there while something is
          unsaved, gone once it isn't. It also shows for defaults that have
          NEVER been saved, with nothing edited — the General tab asks for
          exactly that, and without this there would be no button to do it.
          That case hides on Credentials, which saves each portal itself; a
          real unsaved edit follows the admin to every tab. */}
      {dirty || (neverSaved && section !== "credentials") ? (
        <UnsavedChangesBar
          message={
            dirty
              ? "Unsaved changes"
              : "Payroll settings haven't been saved yet — save to confirm these defaults."
          }
          saving={saving}
          error={saveError}
          onSave={() => void save()}
          onDiscard={dirty ? discard : undefined}
          saveLabel={dirty ? "Save changes" : "Save payroll settings"}
        />
      ) : null}
    </form>
  );
}

function Field({
  id,
  label,
  value,
  onChange,
  placeholder,
  hint,
  type = "text",
  required = false,
}: {
  id: string;
  label: string;
  value: string | null;
  onChange: (value: string | null) => void;
  placeholder?: string;
  hint?: string;
  type?: string;
  // One of the fields a tab's "Required" is counting. Marked on the label,
  // and outlined red while empty, so the red tab points at something — a red
  // tab over a page of unmarked boxes leaves the admin guessing which.
  required?: boolean;
}) {
  const missing = required && !value?.trim();
  const errorId = `${id}-required`;

  return (
    <div>
      <label className={LABEL} htmlFor={id}>
        {label}
        {required ? (
          <span className="ml-0.5 text-destructive" aria-hidden>
            *
          </span>
        ) : null}
      </label>
      <input
        id={id}
        type={type}
        className={`${INPUT} ${missing ? "border-destructive focus-visible:ring-destructive" : ""}`}
        placeholder={placeholder}
        value={value ?? ""}
        // aria-required, not `required`: the native attribute would make the
        // browser refuse to save until all four are filled, and saving what
        // you have so far is a normal thing to do.
        aria-required={required || undefined}
        aria-invalid={missing || undefined}
        aria-describedby={missing ? errorId : undefined}
        // An emptied field means "no value on file", not an empty string —
        // the backend treats null and "" differently on some of these.
        onChange={(e) => onChange(e.target.value === "" ? null : e.target.value)}
      />
      {missing ? (
        <p id={errorId} className="mt-1 text-xs font-medium text-destructive">
          Required
        </p>
      ) : null}
      {hint ? <p className={HINT}>{hint}</p> : null}
    </div>
  );
}

// A yes/no setting, as a card row: the question on the left, the control on
// the right. Ported from the reference's `Toggle` — it reads as a decision
// rather than as a stray tickbox floating in a card.
function Toggle({
  id,
  label,
  hint,
  checked,
  onChange,
  yesLabel = "Yes",
}: {
  id: string;
  label: string;
  hint?: string;
  checked: boolean;
  onChange: (value: boolean) => void;
  yesLabel?: string;
}) {
  return (
    <label
      htmlFor={id}
      className="group inline-flex min-h-11 w-full cursor-pointer items-center justify-between gap-4 rounded-2xl border border-border/70 bg-card px-4 py-2.5 text-sm shadow-sm transition hover:border-primary/40"
    >
      <span className="flex flex-col">
        <span className="font-medium text-foreground">{label}</span>
        {hint ? (
          <span className="text-xs font-normal text-muted-foreground">{hint}</span>
        ) : null}
      </span>

      <span className="inline-flex shrink-0 items-center gap-2">
        <CheckBox id={id} checked={checked} onChange={onChange} />
        <span className="font-medium text-foreground">{yesLabel}</span>
      </span>
    </label>
  );
}

// The four things an admin sets up, in the order they matter: how pay is
// calculated, who the employer is for filing, the portal logins, and the
// accounting hand-off. One at a time rather than one long scroll — they are
// filled at different times by different people.
type SettingsSection = "general" | "formE" | "credentials" | "xero";

// Mirrors the reference's TabPill contract rather than inventing one.
//
//   required + incomplete → a red ring, a red dot, "Required fields missing"
//   required + complete   → "Completed"
//   optional              → never red; "Optional", or a count once saved
//
// The red is the point: it says THIS TAB IS BLOCKING A STATUTORY DOCUMENT,
// which is a different claim from "you have not filled this in yet", and an
// optional tab must never make it.
type SectionStatus = {
  complete: boolean;
  optional?: boolean;
  // Subtitle override for an optional section that does have something in it.
  savedLabel?: string;
  // For a required section whose "incomplete" is not about empty fields: the
  // pill's short text and its tooltip, instead of "Required".
  pendingLabel?: string;
  pendingTitle?: string;
};

const SECTIONS: { id: SettingsSection; label: string }[] = [
  {
    id: "general",
    label: "General",
  },
  {
    id: "formE",
    label: "Form E (LHDN)",
  },
  {
    id: "credentials",
    label: "Credentials",
  },
  {
    id: "xero",
    label: "Xero sync",
  },
];


// A stored blob written by an older build must not take the page down.
function parseMapping(json: string | null): PayrollXeroMapping {
  if (!json) return emptyXeroMapping();

  try {
    return { ...emptyXeroMapping(), ...(JSON.parse(json) as Partial<PayrollXeroMapping>) };
  } catch {
    return emptyXeroMapping();
  }
}

function SectionPicker({
  value,
  onChange,
  status,
  xeroConnected,
}: {
  value: SettingsSection;
  onChange: (next: SettingsSection) => void;
  status: Record<SettingsSection, SectionStatus>;
  // Xero is hidden outright until connected, as the reference does — a
  // mapping that could never post is worse than no tab at all.
  xeroConnected: boolean;
}) {
  return (
    <nav
      role="tablist"
      className="flex flex-wrap gap-2"
      aria-label="Payroll settings sections"
    >
      {SECTIONS.filter((entry) => entry.id !== "xero" || xeroConnected).map((entry) => {
        const active = entry.id === value;
        const state = status[entry.id];

        // Optional sections skip the red treatment entirely — nothing
        // downstream depends on them.
        const blocking = !state.complete && !state.optional;

        // The status now rides on a dot + the accessible name rather than a
        // second line of text, so the control is the same compact pill the run
        // detail's tabs use. Red still means "this is blocking a statutory
        // document"; a quiet green means done; grey means optional-and-untouched.
        const statusLabel = state.optional
          ? state.complete
            ? (state.savedLabel ?? "Saved")
            : "Optional"
          : state.complete
            ? "Completed"
            : (state.pendingTitle ?? "Required fields missing");

        return (
          <button
            key={entry.id}
            type="button"
            role="tab"
            aria-selected={active}
            aria-current={active ? "page" : undefined}
            title={statusLabel}
            onClick={() => onChange(entry.id)}
            className={[
              "flex shrink-0 items-center gap-2 rounded-full border px-4 py-1.5 text-xs font-semibold transition active:scale-[0.98]",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background",
              active
                ? "border-primary bg-primary text-primary-foreground"
                : blocking
                  ? // A required section with something still missing turns the
                    // WHOLE pill red — the point is that "you must fill this in"
                    // can't be scrolled past or mistaken for an optional tab.
                    "border-destructive bg-destructive/10 text-destructive hover:bg-destructive/15"
                  : "border-border/60 bg-card text-muted-foreground hover:text-foreground",
            ].join(" ")}
          >
            {blocking ? (
              <CircleAlert
                aria-hidden
                className={`size-3.5 shrink-0 ${active ? "text-primary-foreground" : "text-destructive"}`}
              />
            ) : (
              <span
                aria-hidden
                className={`size-1.5 shrink-0 rounded-full ${
                  active
                    ? "bg-primary-foreground/70"
                    : state.complete
                      ? "bg-emerald-500"
                      : "bg-muted-foreground/40"
                }`}
              />
            )}
            {entry.label}
            {blocking ? (
              <span className={active ? "text-primary-foreground/80" : "text-destructive/80"}>
                · {state.pendingLabel ?? "Required"}
              </span>
            ) : null}
            <span className="sr-only"> — {statusLabel}</span>
          </button>
        );
      })}
    </nav>
  );
}

// A statutory figure an admin cannot change. Rendered as text rather than a
// disabled input — a greyed-out box still reads as something you could
// switch on.
function Locked({ label, value, note }: { label: string; value: string; note: string }) {
  return (
    <div>
      <span className={LABEL}>{label}</span>
      <p className="mt-1 flex h-12 items-center rounded-2xl border border-border/60 bg-muted/40 px-4 text-sm font-semibold tabular-nums text-foreground">
        {value}
      </p>
      <p className={HINT}>{note}</p>
    </div>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-border/60 bg-muted/30 px-4 py-3">
      <dt className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </dt>
      <dd className="mt-0.5 text-sm font-semibold text-foreground">{value}</dd>
    </div>
  );
}

