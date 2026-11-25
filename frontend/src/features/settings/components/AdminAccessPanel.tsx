import { useState } from "react";
import { LoaderCircle, ShieldCheck, ShieldOff, UserPlus } from "lucide-react";
import { getAdmins, getModuleAccess, removeAdmin, type AdminAccess } from "../api";
import {
  STAFF_ROLES,
  toUpdateEmployee,
  updateEmployee,
  type Employee,
} from "@/features/employees/api";
import { isAdminRole, staffRole, userKind } from "@/features/employees/lib/user-kind";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { useConfirm } from "@/shared/components/ConfirmDialog";
import { SkeletonPanel } from "@/shared/components/Skeleton";
import { AccessDialog, describeAccess } from "./AdminsSettings";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const BUTTON =
  "inline-flex h-10 items-center gap-1.5 rounded-xl bg-primary px-4 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:opacity-90 disabled:opacity-50";
const BUTTON_GHOST =
  "inline-flex h-10 items-center gap-1.5 rounded-xl border border-border/70 bg-card px-3.5 text-sm font-semibold text-foreground transition hover:bg-muted disabled:opacity-50";

// A user's Admin access, on their record (the Owner's view only). The same
// limits as Settings › Admins, reached from the person instead of from a list
// of admins:
//   · staff with no admin access → Give admin access (they stay on the staff);
//   · an admin → their access, Edit access, Remove admin access;
//   · an admin who is admin only → also Create employee record, which puts
//     them on the staff (Employee & admin).
// An Owner's access is everything and can't be narrowed.
export function AdminAccessPanel({
  employee,
  onChanged,
  onRemoved,
}: {
  employee: Employee;
  onChanged: (updated: Employee) => void;
  // An admin-only person removed as an admin leaves the company.
  onRemoved?: () => void;
}) {
  const kind = userKind(employee);
  const isOwnerSeat = employee.role === "Owner";
  const adminsQuery = useCachedQuery("/organizations/admins", getAdmins);
  const modulesQuery = useCachedQuery("/organizations/modules", getModuleAccess);
  const access = (adminsQuery.data ?? []).find((a) => a.userId === employee.id) ?? null;
  const [editing, setEditing] = useState<AdminAccess | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [hireRole, setHireRole] = useState<string>(STAFF_ROLES[0]);
  const [confirm, confirmDialog] = useConfirm();
  const who = employee.name || employee.email;

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError(null);
    try {
      await action();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  }

  async function giveAdminAccess() {
    const ok = await confirm({
      title: `Give ${who} admin access?`,
      message: `They stay on the staff as ${(staffRole(employee) ?? "employee").toLowerCase()} — still paid, with their own employee portal — and can also run the company's admin portal. They start with full access; set their limits next. They can't change their own pay or records from the admin portal.`,
      confirmLabel: "Give admin access",
    });
    if (!ok) return;
    await run(async () => {
      const updated = await updateEmployee(employee.id, { ...toUpdateEmployee(employee), role: "Admin" });
      onChanged(updated);
      const admins = await getAdmins();
      void adminsQuery.refresh();
      const fresh = admins.find((a) => a.userId === employee.id);
      if (fresh) setEditing(fresh);
    });
  }

  async function removeAdminAccess() {
    const employed = kind === "both";
    const ok = await confirm({
      title: employed ? `Remove ${who}'s admin access?` : `Remove ${who} from this company?`,
      message: employed
        ? `They lose the admin portal straight away and stay on the staff as ${(staffRole(employee) ?? "employee").toLowerCase()}.`
        : "They're admin only, so removing their admin access removes them from this company. Their login isn't deleted — if they administer another company, that stays.",
      confirmLabel: employed ? "Remove admin access" : "Remove from company",
      destructive: true,
    });
    if (!ok) return;
    await run(async () => {
      await removeAdmin(employee.id);
      await adminsQuery.refresh();
      if (employed) {
        onChanged({ ...employee, role: staffRole(employee) ?? "Employee", employmentRole: staffRole(employee) });
      } else {
        onRemoved?.();
      }
    });
  }

  async function createEmployeeRecord() {
    const ok = await confirm({
      title: `Create an employee record for ${who}?`,
      message: `They join the staff as ${hireRole.toLowerCase()}: on payroll, under a policy, with leave, claims and attendance in their own employee portal — and keep their admin access. Fill in their payroll details on the record next.`,
      confirmLabel: "Create employee record",
    });
    if (!ok) return;
    await run(async () => {
      const updated = await updateEmployee(employee.id, {
        ...toUpdateEmployee(employee),
        role: employee.role,
        employmentRole: hireRole,
      });
      onChanged(updated);
      await adminsQuery.refresh();
    });
  }

  if (adminsQuery.loading && !adminsQuery.data) return <SkeletonPanel />;

  return (
    <div className="space-y-4">
      {confirmDialog}

      <section className={CARD}>
        <header className="mb-4 flex flex-wrap items-start justify-between gap-3">
          <div>
            <h3 className="flex items-center gap-2 text-base font-black text-foreground">
              <ShieldCheck className="h-4 w-4 text-primary" aria-hidden />
              Admin access
            </h3>
            <p className="mt-0.5 text-sm text-muted-foreground">
              {kind === "employee"
                ? "No admin access. They use the employee portal only."
                : isOwnerSeat
                  ? "Owner — full access to everything, which can't be narrowed."
                  : access
                    ? describeAccess(access)
                    : "Admin"}
            </p>
          </div>

          <div className="flex flex-wrap gap-2">
            {kind === "employee" ? (
              <button type="button" className={BUTTON} disabled={busy} onClick={() => void giveAdminAccess()}>
                {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <ShieldCheck className="h-4 w-4" />}
                Give admin access
              </button>
            ) : null}
            {isAdminRole(employee.role) && !isOwnerSeat && access ? (
              <>
                <button type="button" className={BUTTON_GHOST} disabled={busy} onClick={() => setEditing(access)}>
                  Edit access
                </button>
                <button
                  type="button"
                  className={`${BUTTON_GHOST} text-destructive hover:border-destructive`}
                  disabled={busy}
                  onClick={() => void removeAdminAccess()}
                >
                  <ShieldOff className="h-4 w-4" />
                  Remove admin access
                </button>
              </>
            ) : null}
          </div>
        </header>

        {kind === "both" ? (
          <p className="rounded-2xl border border-border/60 bg-muted/40 p-3 text-xs leading-5 text-muted-foreground">
            They're also on the staff as {(staffRole(employee) ?? "").toLowerCase()}. In the admin portal they can't
            change their own salary, payroll adjustments, leave or other records — another admin or the owner does
            that. They switch to their own employee portal from the account menu.
          </p>
        ) : null}
        {kind === "admin" && !isOwnerSeat ? (
          <p className="rounded-2xl border border-border/60 bg-muted/40 p-3 text-xs leading-5 text-muted-foreground">
            The access above is about managing <strong>other people</strong> — for example running payroll for the
            staff. {who} isn't on the staff: not paid by payroll, no policy, leave or claims of their own.
          </p>
        ) : null}

        {error ? <p className="mt-3 text-sm font-medium text-destructive">{error}</p> : null}
      </section>

      {kind === "admin" ? (
        <section className={CARD}>
          <h3 className="flex items-center gap-2 text-base font-black text-foreground">
            <UserPlus className="h-4 w-4 text-primary" aria-hidden />
            Employee record
          </h3>
          <p className="mt-0.5 text-sm text-muted-foreground">
            None — {who} is admin only. Create one to put them on the staff as well (Employee &amp; admin).
          </p>
          <div className="mt-4 flex flex-wrap items-center gap-2">
            <select
              aria-label="Staff role"
              className="h-10 rounded-xl border border-border/70 bg-card px-3 text-sm text-foreground"
              value={hireRole}
              disabled={busy}
              onChange={(e) => setHireRole(e.target.value)}
            >
              {STAFF_ROLES.map((r) => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
            </select>
            <button type="button" className={BUTTON} disabled={busy} onClick={() => void createEmployeeRecord()}>
              <UserPlus className="h-4 w-4" />
              Create employee record
            </button>
          </div>
        </section>
      ) : null}

      {editing ? (
        <AccessDialog
          admin={editing}
          allModules={modulesQuery.data?.all ?? []}
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
