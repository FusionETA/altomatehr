import { useState } from "react";
import { createPortal } from "react-dom";
import { LoaderCircle, ShieldCheck, X } from "lucide-react";
import { getAdmins, getModuleAccess, setAdminAccess, type AdminAccess } from "../api";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonPanel } from "@/shared/components/Skeleton";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";

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

// Owner-only. Controls which modules each Admin in the org can see. The backend
// enforces it (plan ceiling ∩ this grant), so a removed module 403s on access —
// this is where the Owner decides that.
export function AdminsSettings() {
  const adminsQuery = useCachedQuery("/organizations/admins", getAdmins);
  const modulesQuery = useCachedQuery("/organizations/modules", getModuleAccess);
  const [editing, setEditing] = useState<AdminAccess | null>(null);

  const admins = adminsQuery.data ?? [];
  const allModules = modulesQuery.data?.all ?? [];

  return (
    <div className={`${CARD} space-y-5`}>
      <div>
        <h2 className="text-lg font-black text-foreground">Admins &amp; access</h2>
        <p className="text-sm text-muted-foreground">
          Choose which modules each admin can see. Owners always have full access; this only
          narrows Admins, and never beyond what the org's plan already enables.
        </p>
      </div>

      {adminsQuery.error ? (
        <p className="text-sm font-medium text-destructive">{adminsQuery.error}</p>
      ) : null}

      {adminsQuery.loading ? (
        <SkeletonPanel />
      ) : admins.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          No admins yet. Set an employee's role to Admin to manage their access here.
        </p>
      ) : (
        <ul className="divide-y divide-border/60 overflow-hidden rounded-2xl border border-border/60">
          {admins.map((admin) => (
            <li key={admin.userId} className="flex items-center justify-between gap-3 px-4 py-3">
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

        <label className="mt-4 flex items-start gap-2 text-sm font-medium text-foreground">
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
          <div className="mt-3 grid grid-cols-2 gap-2 rounded-2xl border border-border/60 bg-background/60 p-3">
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
