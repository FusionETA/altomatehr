import { useEffect, useState } from "react";
import {
  Building2,
  CalendarDays,
  FolderKanban,
  Network,
  ShieldCheck,
  Users,
  Wallet,
  type LucideIcon,
} from "lucide-react";
import type { SignedInUser } from "@/shared/types/session";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { getEmployees } from "@/features/employees/api";
import { getTeams } from "@/features/teams/api";
import { getPolicies } from "@/features/policies/api";
import { getAccounts, getProjects } from "@/features/settings/api";
import { getLeaveTypes } from "@/features/leave/api";

type Metric = { label: string; value: number; icon: LucideIcon };

type QuickLink = {
  parent: string;
  child: string;
  label: string;
  hint: string;
  icon: LucideIcon;
};

const quickLinks: QuickLink[] = [
  { parent: "company", child: "manage-employee", label: "Manage Employee", hint: "Roles, supervisors & policies", icon: Users },
  { parent: "company", child: "teams", label: "Teams", hint: "Hierarchy & approval chains", icon: Network },
  { parent: "settings", child: "settings-organization", label: "Organization", hint: "Company profile & geofence", icon: Building2 },
  { parent: "settings", child: "settings-policies", label: "Policies", hint: "Enforcement & entitlements", icon: ShieldCheck },
  { parent: "settings", child: "settings-accounts", label: "Accounts", hint: "Spend limits & mileage", icon: Wallet },
  { parent: "settings", child: "settings-projects", label: "Projects", hint: "Sites & geofence centres", icon: FolderKanban },
];

export function AdminOverview({
  user,
  onOpen,
}: {
  user: SignedInUser;
  onOpen: (parentId: string, childId: string) => void;
}) {
  const [metrics, setMetrics] = useState<Metric[] | null>(null);

  useEffect(() => {
    Promise.all([
      getEmployees().catch(() => []),
      getProjects().catch(() => []),
      getTeams().catch(() => []),
      getPolicies().catch(() => []),
      getAccounts().catch(() => []),
      getLeaveTypes().catch(() => []),
    ]).then(([employees, projects, teams, policies, accounts, leaveTypes]) => {
      setMetrics([
        { label: "Employees", value: employees.length, icon: Users },
        { label: "Projects", value: projects.length, icon: FolderKanban },
        { label: "Teams", value: teams.length, icon: Network },
        { label: "Policies", value: policies.length, icon: ShieldCheck },
        { label: "Accounts", value: accounts.length, icon: Wallet },
        { label: "Leave types", value: leaveTypes.length, icon: CalendarDays },
      ]);
    });
  }, []);

  return (
    <div className="space-y-6">
      <section className="rounded-[28px] border border-border/70 bg-card/90 p-6 shadow-ambient backdrop-blur-sm">
        <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">
          Admin Portal
        </p>
        <h2 className="mt-2 text-2xl font-bold text-foreground">
          Welcome back, {buildName(user.email)}
        </h2>
        <p className="mt-2 max-w-2xl text-sm leading-6 text-muted-foreground">
          Configure the organization, its people, and the rules that govern claims,
          attendance and leave. Jump straight to a workspace below.
        </p>
      </section>

      {/* Live org metrics from the existing endpoints. */}
      <div className="grid gap-3 grid-cols-2 lg:grid-cols-3">
        {(metrics ?? Array.from({ length: 6 }, () => null)).map((m, i) => {
          const Icon = m?.icon;
          return (
            <div
              key={m?.label ?? i}
              className="flex items-center justify-between gap-3 rounded-[24px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm"
            >
              <div className="min-w-0">
                <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                  {m?.label ?? "—"}
                </p>
                <p className="mt-1 text-3xl font-black tracking-tight text-foreground">
                  {m ? m.value : "…"}
                </p>
              </div>
              {Icon ? (
                <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
                  <Icon className="h-5 w-5" />
                </span>
              ) : null}
            </div>
          );
        })}
      </div>

      <div>
        <p className="mb-2 text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
          Jump to
        </p>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {quickLinks.map((link) => {
            const Icon = link.icon;
            return (
              <button
                key={`${link.parent}:${link.child}`}
                type="button"
                onClick={() => onOpen(link.parent, link.child)}
                className="flex items-start gap-3 rounded-[24px] border border-border/70 bg-card/90 p-5 text-left shadow-ambient backdrop-blur-sm transition hover:border-primary/40 hover:text-primary"
              >
                <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
                  <Icon className="h-5 w-5" />
                </span>
                <span className="min-w-0">
                  <span className="block text-sm font-bold text-foreground">{link.label}</span>
                  <span className="mt-0.5 block text-xs text-muted-foreground">{link.hint}</span>
                </span>
              </button>
            );
          })}
        </div>
      </div>
    </div>
  );
}
