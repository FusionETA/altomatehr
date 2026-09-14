import { useState } from "react";
import { createPortal } from "react-dom";
import { LoaderCircle, ShieldCheck, UserPlus, X } from "lucide-react";
import { getAdmins, getModuleAccess, setAdminAccess, type AdminAccess } from "../api";
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
};
const moduleLabel = (m: string) => MODULE_LABELS[m] ?? m;

// Owner-only. Controls who is an Admin in the org and which modules each can see.
// The backend enforces it (plan ceiling ∩ this grant), so a removed module 403s
// on access — this is where the Owner decides that.
export function AdminsSettings() {
  const adminsQuery = useCachedQuery("/organizations/admins", getAdmins);
  const modulesQuery = useCachedQuery("/organizations/modules", getModuleAccess);
  const [editing, setEditing] = useState<AdminAccess | null>(null);
  const [tab, setTab] = useState<"manage" | "add">("manage");

  const admins = adminsQuery.data ?? [];
  const allModules = modulesQuery.data?.all ?? [];

  return (
    <div className={`${CARD} space-y-5`}>
      <div>
        <h2 className="text-lg font-black text-foreground">Admins &amp; access</h2>
        <p className="text-sm text-muted-foreground">
          Add admins and choose which modules each can see. Owners always have full access; a
          grant only narrows Admins, and never beyond what the org's plan already enables.
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
                        admin.modules && admin.modules.length === 0
                          ? "text-destructive"
                          : "text-muted-foreground"
                      }`}
                    >
                      <ShieldCheck className="h-3 w-3" />
                      {admin.modules === null
                        ? "Full access"
                        : admin.modules.length === 0
                          ? "No modules"
                          : `${admin.modules.length} module${admin.modules.length === 1 ? "" : "s"}: ${admin.modules.map(moduleLabel).join(", ")}`}
                    </span>
                  </div>
                  <button
                    type="button"
                    onClick={() => setEditing(admin)}
                    className="shrink-0 rounded-full border border-border/60 bg-card px-4 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground"
                  >
                    Manage access
                  </button>
                </li>
              ))}
            </ul>
          )}
        </>
      ) : (
        <AddAdminForm
          allModules={allModules}
          onCreated={() => {
            setTab("manage");
            void adminsQuery.refresh();
          }}
        />
      )}

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

// Full-access toggle + a checklist of module keys. `selected` only matters while
// full access is off.
function ModuleChecklist({
  allModules,
  fullAccess,
  setFullAccess,
  selected,
  setSelected,
}: {
  allModules: string[];
  fullAccess: boolean;
  setFullAccess: (v: boolean) => void;
  selected: Set<string>;
  setSelected: (updater: (prev: Set<string>) => Set<string>) => void;
}) {
  return (
    <div className="space-y-3">
      <label className="flex items-start gap-2 text-sm font-medium text-foreground">
        <input
          type="checkbox"
          checked={fullAccess}
          onChange={(e) => setFullAccess(e.target.checked)}
          className="mt-0.5 h-4 w-4 rounded border-border accent-primary"
        />
        <span>
          Full access
          <span className="block text-xs font-normal text-muted-foreground">
            Everything the org's plan enables.
          </span>
        </span>
      </label>

      {!fullAccess ? (
        <div className="grid grid-cols-2 gap-2 rounded-2xl border border-border/60 bg-background/60 p-3">
          {allModules.map((m) => (
            <label key={m} className="flex items-center gap-2 text-sm text-foreground">
              <input
                type="checkbox"
                checked={selected.has(m)}
                onChange={(e) =>
                  setSelected((prev) => {
                    const next = new Set(prev);
                    if (e.target.checked) next.add(m);
                    else next.delete(m);
                    return next;
                  })
                }
                className="h-4 w-4 rounded border-border accent-primary"
              />
              {moduleLabel(m)}
            </label>
          ))}
        </div>
      ) : null}
    </div>
  );
}

// Create a brand-new admin by email, or add an existing account (from another
// org) as an admin here. Scope defaults to full access; untick to restrict.
// Mirrors the monolith's "Add admin" form (POST /employees with role Admin +
// a module grant), which reuses the identity when the email already exists.
function AddAdminForm({
  allModules,
  onCreated,
}: {
  allModules: string[];
  onCreated: () => void;
}) {
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [fullAccess, setFullAccess] = useState(true);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!email.trim()) return;
    setSaving(true);
    setError(null);
    try {
      await createEmployee({
        email: email.trim(),
        name: name.trim() || undefined,
        password: password.trim() || undefined,
        role: "Admin",
        modules: fullAccess ? null : [...selected],
      });
      onCreated();
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
        password and share it out-of-band; they can change it after first sign-in.
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

      <div className="space-y-2">
        <p className="flex items-center gap-1.5 text-sm font-semibold text-foreground">
          <ShieldCheck className="h-4 w-4 text-primary" />
          Access scope
        </p>
        <ModuleChecklist
          allModules={allModules}
          fullAccess={fullAccess}
          setFullAccess={setFullAccess}
          selected={selected}
          setSelected={setSelected}
        />
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
  const [fullAccess, setFullAccess] = useState(admin.modules === null);
  const [selected, setSelected] = useState<Set<string>>(
    () => new Set(admin.modules ?? allModules),
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save() {
    setSaving(true);
    setError(null);
    try {
      await setAdminAccess(admin.userId, fullAccess ? null : [...selected]);
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save access.");
    } finally {
      setSaving(false);
    }
  }

  return createPortal(
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm"
      onClick={onClose}
    >
      <div
        className="w-full max-w-md rounded-[28px] border border-border/70 bg-card p-6 shadow-2xl"
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

        <div className="mt-4">
          <ModuleChecklist
            allModules={allModules}
            fullAccess={fullAccess}
            setFullAccess={setFullAccess}
            selected={selected}
            setSelected={setSelected}
          />
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
            disabled={saving}
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
