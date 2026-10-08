import { useState } from "react";
import { createPortal } from "react-dom";
import { LoaderCircle, ShieldCheck, UserPlus, X } from "lucide-react";
import {
  getAdmins,
  getModuleAccess,
  removeAdmin,
  setAdminAccess,
  type AdminAccess,
  type ModuleLevel,
} from "../api";
import { getPolicies } from "@/features/policies/api";
import { useConfirm } from "@/shared/components/ConfirmDialog";
import { createEmployee } from "@/features/employees/api";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonPanel } from "@/shared/components/Skeleton";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50";

// Friendly labels for the module keys the server grants against.
const MODULE_LABELS: Record<string, string> = {
  employees: "Employees",
  leave: "Leave",
  projects: "Projects",
  teams: "Teams",
  accounts: "Accounts",
  policies: "Policies",
  overtime: "Overtime",
  claims: "Claims",
  attendance: "Attendance",
  payroll: "Payroll",
  audit: "Activity log",
  documents: "Documents",
};
const moduleLabel = (m: string) => MODULE_LABELS[m] ?? m;

// Owner-only. Controls who is an Admin in the org and how far each can go:
// which modules (and whether view-only), which employees (by policy), and
// whether they may change settings. The backend enforces all three — this is
// where the Owner decides them. No named roles: the Owner builds each admin.
export function AdminsSettings() {
  const adminsQuery = useCachedQuery("/organizations/admins", getAdmins);
  const modulesQuery = useCachedQuery("/organizations/modules", getModuleAccess);
  const [editing, setEditing] = useState<AdminAccess | null>(null);
  const [tab, setTab] = useState<"manage" | "add">("manage");
  const [confirm, confirmDialog] = useConfirm();
  const [removingId, setRemovingId] = useState<string | null>(null);
  const [removeError, setRemoveError] = useState<string | null>(null);

  async function handleRemove(admin: AdminAccess) {
    const who = admin.name || admin.email;
    const ok = await confirm({
      title: `Remove ${who} as an admin?`,
      message:
        "They lose access to this company straight away. Their login isn't deleted — if they administer another company, that stays. You can add them again later.",
      confirmLabel: "Remove admin",
      destructive: true,
    });
    if (!ok) return;
    setRemovingId(admin.userId);
    setRemoveError(null);
    try {
      await removeAdmin(admin.userId);
      await adminsQuery.refresh();
    } catch (err) {
      setRemoveError(err instanceof Error ? err.message : "Could not remove the admin.");
    } finally {
      setRemovingId(null);
    }
  }

  // The list carries Owners too, so the page does not read "no admins" while an
  // Owner runs the org. They are shown read-only: an Owner's access cannot be
  // narrowed, and the backend refuses a grant for one. The count is Admins only.
  const members = adminsQuery.data ?? [];
  const owners = members.filter((m) => m.role === "Owner");
  const admins = members.filter((m) => m.role !== "Owner");
  const allModules = modulesQuery.data?.all ?? [];

  return (
    <div className={`${CARD} space-y-5`}>
      <div>
        <h2 className="text-lg font-black text-foreground">Admins &amp; access</h2>
        <p className="text-sm text-muted-foreground">
          Add admins and decide, for each, which modules they can use (view only or manage),
          which employees they cover, and whether they can change settings. Owners always have
          full access; limits only narrow Admins, never beyond what the org's plan enables.
        </p>
      </div>

      <div
        role="tablist"
        aria-label="Admin management"
        className="inline-flex rounded-xl border border-border/60 bg-muted/50 p-1 text-sm"
      >
        <button
          type="button"
          role="tab"
          aria-selected={tab === "manage"}
          onClick={() => setTab("manage")}
          className={`rounded-lg px-4 py-1.5 font-semibold transition-colors ${
            tab === "manage"
              ? "bg-primary text-primary-foreground shadow-sm"
              : "text-muted-foreground hover:text-foreground"
          }`}
        >
          Manage admins ({admins.length})
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === "add"}
          onClick={() => setTab("add")}
          className={`rounded-lg px-4 py-1.5 font-semibold transition-colors ${
            tab === "add"
              ? "bg-primary text-primary-foreground shadow-sm"
              : "text-muted-foreground hover:text-foreground"
          }`}
        >
          Add admin
        </button>
      </div>

      {tab === "manage" ? (
        <>
          {adminsQuery.error ? (
            <p className="text-sm font-medium text-destructive">{adminsQuery.error}</p>
          ) : null}
          {removeError ? <p className="text-sm font-medium text-destructive">{removeError}</p> : null}

          {!adminsQuery.loading && owners.length > 0 ? (
            <ul className="divide-y divide-border/60 overflow-hidden rounded-2xl border border-border/60 bg-muted/30">
              {owners.map((owner) => (
                <li key={owner.userId} className="flex items-center justify-between gap-3 px-4 py-3">
                  <div className="min-w-0">
                    <p className="truncate font-semibold text-foreground">
                      {owner.name || owner.email}
                    </p>
                    <span className="inline-flex items-center gap-1 text-xs text-muted-foreground">
                      <ShieldCheck className="h-3 w-3" />
                      Owner · full access
                    </span>
                  </div>
                  <span className="shrink-0 text-xs font-medium text-muted-foreground">
                    Can't be restricted
                  </span>
                </li>
              ))}
            </ul>
          ) : null}

          {adminsQuery.loading ? (
            <SkeletonPanel />
          ) : admins.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              No admins yet. Add one from the "Add admin" tab.
            </p>
          ) : (
            <ul className="divide-y divide-border/60 overflow-hidden rounded-2xl border border-border/60">
              {admins.map((admin) => (
                <li
                  key={admin.userId}
                  className="flex items-center justify-between gap-3 px-4 py-3"
                >
                  <div className="min-w-0">
                    <p className="truncate font-semibold text-foreground">
                      {admin.name || admin.email}
                    </p>
                    <span
                      className={`inline-flex items-center gap-1 text-xs ${
                        admin.levels && Object.keys(admin.levels).length === 0
                          ? "text-destructive"
                          : "text-muted-foreground"
                      }`}
                    >
                      <ShieldCheck className="h-3 w-3 shrink-0" />
                      <span className="truncate">{describeAccess(admin)}</span>
                    </span>
                  </div>
                  <div className="flex shrink-0 items-center gap-2">
                    <button
                      type="button"
                      onClick={() => setEditing(admin)}
                      className="rounded-full border border-border/60 bg-card px-4 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground"
                    >
                      Manage access
                    </button>
                    <button
                      type="button"
                      onClick={() => void handleRemove(admin)}
                      disabled={removingId === admin.userId}
                      className="inline-flex items-center gap-1 rounded-full border border-destructive/30 bg-card px-3 py-1.5 text-xs font-semibold text-destructive transition-colors hover:bg-destructive/10 disabled:opacity-50"
                    >
                      {removingId === admin.userId ? <LoaderCircle className="h-3 w-3 animate-spin" /> : null}
                      Remove
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </>
      ) : (
        <AddAdminForm
          onCreated={(created) => {
            // Straight into their limits: a new admin starts with full access.
            setTab("manage");
            setEditing(created);
            void adminsQuery.refresh();
          }}
        />
      )}

      {confirmDialog}

      {editing ? (
        <AccessDialog
          admin={editing}
          allModules={allModules}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void adminsQuery.refresh();
          }}
        />
      ) : null}
    </div>
  );
}

// One line per admin: what they can reach, at a glance.
function describeAccess(admin: AdminAccess): string {
  const levels = admin.levels;
  const parts: string[] = [];
  if (levels === null) {
    parts.push("All modules");
  } else {
    const on = Object.entries(levels).filter(([, l]) => l !== "None");
    if (on.length === 0) return "No modules";
    const views = on.filter(([, l]) => l === "View").length;
    parts.push(
      `${on.length} module${on.length === 1 ? "" : "s"}${views > 0 ? ` (${views} view only)` : ""}`,
    );
  }
  parts.push(
    admin.policyIds === null
      ? "all employees"
      : `${admin.policyIds.length} polic${admin.policyIds.length === 1 ? "y" : "ies"}`,
  );
  if (!admin.canChangeSettings) parts.push("no settings");
  if (levels === null && admin.policyIds === null && admin.canChangeSettings) return "Full access";
  return parts.join(" · ");
}

// Create a brand-new admin by email, or add an existing account (from another
// org) as an admin here. They start with full access; the caller opens
// Manage access straight after, where the Owner sets their limits.
// Mirrors the monolith's "Add admin" form (POST /employees with role Admin),
// which reuses the identity when the email already exists.
function AddAdminForm({ onCreated }: { onCreated: (admin: AdminAccess) => void }) {
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!email.trim()) return;
    setSaving(true);
    setError(null);
    try {
      const created = await createEmployee({
        email: email.trim(),
        name: name.trim() || undefined,
        password: password.trim() || undefined,
        role: "Admin",
        modules: null,
      });
      onCreated({
        userId: created.id,
        name: created.name,
        email: created.email,
        role: "Admin",
        modules: null,
        levels: null,
        policyIds: null,
        canChangeSettings: true,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not add the admin.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-5 rounded-2xl border border-border/60 bg-background/40 p-4 sm:p-5">
      <p className="text-xs leading-5 text-muted-foreground">
        Add by email. If the email already belongs to an account, that identity is reused — no new
        login is created and the password is ignored. For a brand-new admin, set a temporary
        password and share it out-of-band; they can change it after first sign-in. You'll choose
        their access next.
      </p>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-1.5">
          <label htmlFor="admin-name" className="text-sm font-semibold text-foreground">
            Full name
          </label>
          <input
            id="admin-name"
            className={INPUT}
            value={name}
            maxLength={160}
            disabled={saving}
            placeholder="Jane Doe"
            onChange={(e) => setName(e.target.value)}
          />
        </div>
        <div className="space-y-1.5">
          <label htmlFor="admin-email" className="text-sm font-semibold text-foreground">
            Email
          </label>
          <input
            id="admin-email"
            type="email"
            required
            autoComplete="off"
            className={INPUT}
            value={email}
            maxLength={120}
            disabled={saving}
            placeholder="jane@company.com"
            onChange={(e) => setEmail(e.target.value)}
          />
        </div>
        <div className="space-y-1.5 sm:col-span-2">
          <label htmlFor="admin-password" className="text-sm font-semibold text-foreground">
            Temporary password
          </label>
          <input
            id="admin-password"
            type="text"
            autoComplete="off"
            className={INPUT}
            value={password}
            minLength={8}
            maxLength={100}
            disabled={saving}
            placeholder="At least 8 characters (new accounts only)"
            onChange={(e) => setPassword(e.target.value)}
          />
          <p className="text-xs text-muted-foreground">
            Only needed when creating a brand-new admin. Leave blank when adding an existing account.
          </p>
        </div>
      </div>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      <button
        type="submit"
        disabled={saving || !email.trim()}
        className="inline-flex items-center justify-center gap-2 rounded-2xl bg-primary px-5 py-2.5 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:opacity-90 disabled:opacity-50"
      >
        {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <UserPlus className="h-4 w-4" />}
        Add admin
      </button>
    </form>
  );
}

type AccessTab = "modules" | "employees" | "settings";

const LEVELS: { value: ModuleLevel; label: string }[] = [
  { value: "None", label: "Off" },
  { value: "View", label: "View" },
  { value: "Manage", label: "Manage" },
];

// The Owner's three limits for one admin, one tab each.
function AccessDialog({
  admin,
  allModules,
  onClose,
  onSaved,
}: {
  admin: AdminAccess;
  allModules: string[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const policiesQuery = useCachedQuery("/policies", getPolicies);
  const policies = (policiesQuery.data ?? []).filter(
    (p) => !p.isArchived || admin.policyIds?.includes(p.id),
  );

  const [tab, setTab] = useState<AccessTab>("modules");
  const [fullAccess, setFullAccess] = useState(admin.levels === null);
  const [levels, setLevels] = useState<Record<string, ModuleLevel>>(() =>
    Object.fromEntries(
      allModules.map((m) => [m, admin.levels === null ? "Manage" : (admin.levels[m] ?? "None")]),
    ),
  );
  const [allEmployees, setAllEmployees] = useState(admin.policyIds === null);
  const [policyIds, setPolicyIds] = useState<Set<string>>(() => new Set(admin.policyIds ?? []));
  const [canChangeSettings, setCanChangeSettings] = useState(admin.canChangeSettings);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save() {
    setSaving(true);
    setError(null);
    try {
      await setAdminAccess(admin.userId, {
        levels: fullAccess
          ? null
          : Object.fromEntries(Object.entries(levels).filter(([, l]) => l !== "None")),
        policyIds: allEmployees ? null : [...policyIds],
        canChangeSettings,
      });
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save access.");
    } finally {
      setSaving(false);
    }
  }

  const tabs: { id: AccessTab; label: string }[] = [
    { id: "modules", label: "Modules" },
    { id: "employees", label: "Employees" },
    { id: "settings", label: "Settings" },
  ];

  return createPortal(
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm"
      onClick={onClose}
    >
      <div
        className="flex max-h-[calc(100vh-2rem)] w-full max-w-lg flex-col rounded-[28px] border border-border/70 bg-card p-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <h3 className="text-base font-black text-foreground">Manage access</h3>
            <p className="truncate text-xs text-muted-foreground">{admin.name || admin.email}</p>
          </div>
          <button
            type="button"
            aria-label="Close"
            onClick={onClose}
            className="rounded-full p-2 text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div
          role="tablist"
          aria-label="Access"
          className="mt-4 inline-flex self-start rounded-xl border border-border/60 bg-muted/50 p-1 text-sm"
        >
          {tabs.map((t) => (
            <button
              key={t.id}
              type="button"
              role="tab"
              aria-selected={tab === t.id}
              onClick={() => setTab(t.id)}
              className={`rounded-lg px-3.5 py-1.5 font-semibold transition-colors ${
                tab === t.id
                  ? "bg-primary text-primary-foreground shadow-sm"
                  : "text-muted-foreground hover:text-foreground"
              }`}
            >
              {t.label}
            </button>
          ))}
        </div>

        {/* One fixed height for all three tabs, so the dialog is the same size
            whichever is open; the longer module list scrolls inside it. */}
        <div className="nice-scrollbar mt-4 h-[min(30rem,60vh)] shrink-0 overflow-y-auto pr-1">
          {tab === "modules" ? (
            <div className="space-y-3">
              <label className="flex items-start gap-2 text-sm font-medium text-foreground">
                <input
                  type="checkbox"
                  checked={fullAccess}
                  onChange={(e) => setFullAccess(e.target.checked)}
                  className="mt-0.5 h-4 w-4 rounded border-border accent-primary"
                />
                <span>
                  All modules, full control
                  <span className="block text-xs font-normal text-muted-foreground">
                    Everything the org's plan enables, at Manage.
                  </span>
                </span>
              </label>

              {!fullAccess ? (
                <>
                  <p className="text-xs text-muted-foreground">
                    <strong className="text-foreground">View</strong> — see the screens and download
                    what's there. <strong className="text-foreground">Manage</strong> — also edit,
                    approve and run.
                  </p>
                  <ul className="divide-y divide-border/60 rounded-2xl border border-border/60">
                    {allModules.map((m) => (
                      <li key={m} className="flex items-center justify-between gap-3 px-3 py-2">
                        <span className="text-sm font-medium text-foreground">{moduleLabel(m)}</span>
                        <div className="inline-flex rounded-lg border border-border/60 bg-muted/40 p-0.5 text-xs">
                          {LEVELS.map((l) => (
                            <button
                              key={l.value}
                              type="button"
                              aria-pressed={levels[m] === l.value}
                              onClick={() => setLevels((cur) => ({ ...cur, [m]: l.value }))}
                              className={`rounded-md px-2.5 py-1 font-semibold transition-colors ${
                                levels[m] === l.value
                                  ? "bg-card text-foreground shadow-sm"
                                  : "text-muted-foreground hover:text-foreground"
                              }`}
                            >
                              {l.label}
                            </button>
                          ))}
                        </div>
                      </li>
                    ))}
                  </ul>
                </>
              ) : null}
            </div>
          ) : tab === "employees" ? (
            <div className="space-y-3">
              <label className="flex items-start gap-2 text-sm font-medium text-foreground">
                <input
                  type="radio"
                  name="employee-scope"
                  checked={allEmployees}
                  onChange={() => setAllEmployees(true)}
                  className="mt-0.5 h-4 w-4 accent-primary"
                />
                <span>
                  All employees
                  <span className="block text-xs font-normal text-muted-foreground">
                    Sees everyone, and can run company-wide payroll.
                  </span>
                </span>
              </label>
              <label className="flex items-start gap-2 text-sm font-medium text-foreground">
                <input
                  type="radio"
                  name="employee-scope"
                  checked={!allEmployees}
                  onChange={() => setAllEmployees(false)}
                  className="mt-0.5 h-4 w-4 accent-primary"
                />
                <span>
                  Only employees on these policies
                  <span className="block text-xs font-normal text-muted-foreground">
                    Everyone else is hidden — in employees, payroll, claims, leave and attendance.
                  </span>
                </span>
              </label>

              {!allEmployees ? (
                <>
                  {policiesQuery.loading ? (
                    <SkeletonPanel />
                  ) : policies.length === 0 ? (
                    <p className="text-sm text-muted-foreground">No policies yet.</p>
                  ) : (
                    <div className="grid gap-2 rounded-2xl border border-border/60 bg-background/60 p-3 sm:grid-cols-2">
                      {policies.map((p) => (
                        <label key={p.id} className="flex items-center gap-2 text-sm text-foreground">
                          <input
                            type="checkbox"
                            checked={policyIds.has(p.id)}
                            onChange={(e) =>
                              setPolicyIds((prev) => {
                                const next = new Set(prev);
                                if (e.target.checked) next.add(p.id);
                                else next.delete(p.id);
                                return next;
                              })
                            }
                            className="h-4 w-4 rounded border-border accent-primary"
                          />
                          <span className="truncate">
                            {p.name}
                            {p.isDefault ? (
                              <span className="text-xs text-muted-foreground"> (default)</span>
                            ) : null}
                          </span>
                        </label>
                      ))}
                    </div>
                  )}
                  <p className="rounded-2xl border border-warning bg-warning/40 px-3 py-2 text-xs font-medium text-warning-foreground">
                    Payroll covers the whole company, so with limited employees they can view payroll
                    runs (only their people's payslips) but can't create, run or submit them, or
                    download the EPF / SOCSO / PCB files, bank file or annual forms.
                  </p>
                </>
              ) : null}
            </div>
          ) : (
            <div className="space-y-3">
              <label className="flex items-start gap-2 text-sm font-medium text-foreground">
                <input
                  type="checkbox"
                  checked={canChangeSettings}
                  onChange={(e) => setCanChangeSettings(e.target.checked)}
                  className="mt-0.5 h-4 w-4 rounded border-border accent-primary"
                />
                <span>
                  Can change company settings
                  <span className="block text-xs font-normal text-muted-foreground">
                    Turn off to let them work — run payroll, approve, edit employees — without
                    changing how the company is set up.
                  </span>
                </span>
              </label>
              <div className="rounded-2xl border border-border/60 bg-background/60 p-3 text-xs text-muted-foreground">
                <p className="font-semibold text-foreground">This covers</p>
                <ul className="mt-1 list-disc space-y-0.5 pl-4">
                  <li>System Settings → Organization (company details, claim settings)</li>
                  <li>System Settings → Work Schedule (shifts, holidays)</li>
                  <li>Payroll settings, company &amp; statutory info, portal logins</li>
                  <li>The Xero connection</li>
                </ul>
                <p className="mt-2">
                  Accounts, Projects and Policies follow their own module level. Adding admins and
                  choosing their access is always the Owner's.
                </p>
              </div>
            </div>
          )}
        </div>

        {error ? <p className="mt-3 text-sm font-medium text-destructive">{error}</p> : null}

        <div className="mt-5 flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            disabled={saving}
            className="rounded-2xl bg-muted px-4 py-2 text-sm font-semibold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={() => void save()}
            disabled={saving || (!allEmployees && policyIds.size === 0)}
            className="inline-flex items-center gap-2 rounded-2xl bg-primary px-4 py-2 text-sm font-semibold text-primary-foreground transition hover:bg-primary/90 disabled:opacity-50"
          >
            {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
            Save
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
