import type { UrlNav } from "@/shared/lib/use-url-nav";
import {
  Banknote,
  CalendarClock,
  CalendarDays,
  History,
  LayoutDashboard,
  Network,
  Receipt,
  Settings2,
  type LucideIcon,
} from "lucide-react";

// The admin portal nav mirrors the production monolith's admin-shell:
// top-level modules down the sidebar, each expanding to its sub-pages.
// `built: false` items render a "Coming next" placeholder for now —
// the structure matches the real system so the shape is faithful even
// before every admin surface is rebuilt.
//
// `module` is the grant key (OrgModules on the backend) an entry needs. An
// Admin whose "Manage access" grant leaves it out does not see the entry, and
// the endpoints behind it 403. No module = every admin sees it.
export type AdminChild = { id: string; label: string; ownerOnly?: boolean; module?: string };

export type AdminNavItem = {
  id: string;
  label: string;
  icon: LucideIcon;
  built?: boolean;
  module?: string;
  children?: AdminChild[];
};

export const adminNav: AdminNavItem[] = [
  { id: "overview", label: "Executive Overview", icon: LayoutDashboard, built: true },
  { id: "attendance", label: "Attendance", icon: CalendarClock, module: "attendance" },
  { id: "claims", label: "Claims", icon: Receipt, built: true, module: "claims" },
  { id: "payroll", label: "Payroll", icon: Banknote, built: true, module: "payroll" },
  { id: "leave", label: "Leave", icon: CalendarDays, module: "leave" },
  {
    id: "company",
    label: "Company/Employee",
    icon: Network,
    built: true,
    children: [
      { id: "company-structure", label: "Company Structure", module: "teams" },
      { id: "manage-employee", label: "Manage Employee", module: "employees" },
    ],
  },
  { id: "audit", label: "Activity Log", icon: History, module: "audit" },
  {
    id: "settings",
    label: "System Settings",
    icon: Settings2,
    built: true,
    children: [
      { id: "settings-organization", label: "Organization" },
      { id: "settings-accounts", label: "Accounts", module: "accounts" },
      { id: "settings-projects", label: "Projects", module: "projects" },
      { id: "settings-work-schedule", label: "Work Schedule" },
      { id: "settings-policies", label: "Policies", module: "policies" },
      // Owner-only: an Admin cannot edit their own or a peer's access.
      { id: "settings-admins", label: "Admins", ownerOnly: true },
    ],
  },
];

// The default sub-view opened when a parent is selected: its first child,
// or the parent's own id when it has no children.
export function defaultChildOf(item: AdminNavItem): string {
  return item.children?.[0]?.id ?? item.id;
}

export function findNavItem(id: string): AdminNavItem {
  return adminNav.find((item) => item.id === id) ?? adminNav[0];
}

// The nav as the signed-in admin may see it. `enabled` is their effective
// module set (org plan ∩ their grant), or null while it is still loading — in
// which case nothing is hidden: the backend refuses regardless, and hiding
// everything until it lands would blink the sidebar for every admin.
//
// A parent with children is kept only while at least one child survives, so
// Company/Employee disappears for an admin granted neither of its pages.
export function visibleAdminNav(
  isOwner: boolean,
  enabled: ReadonlySet<string> | null,
): AdminNavItem[] {
  const allowed = (module?: string) => !module || enabled === null || enabled.has(module);

  return adminNav.flatMap((item) => {
    if (!allowed(item.module)) return [];
    if (!item.children) return [item];

    const children = item.children.filter(
      (child) => (!child.ownerOnly || isOwner) && allowed(child.module),
    );
    return children.length === 0 ? [] : [{ ...item, children }];
  });
}

// ─── URL navigation ───────────────────────────────────────────────────

// What the shell opens to, and what an unreadable `?v=` falls back to.
export const NAV_FALLBACK: UrlNav = { parent: "overview", child: "overview" };

// The URL is user-editable, so nothing from it is trusted:
//
//   · an unknown parent becomes Overview;
//   · a child that does not belong to its parent becomes that parent's default,
//     or a crafted `?v=overview/settings-admins` would light up one sidebar
//     entry while rendering another's panel;
//   · an ownerOnly child is refused to anyone who is not the Owner. The sidebar
//     already filters those out, so before the URL drove navigation there was
//     no way to ask for one. Now there is, and only this check stands between a
//     typed URL and a panel that was never offered. (The endpoints behind it are
//     `[Authorize(Roles = "Owner")]`, so nothing could be CHANGED either way —
//     but rendering it would still show a list they were not meant to see, and
//     then 403 on everything.)
//
//   · a module the admin's grant leaves out is refused the same way, so a
//     bookmark to Payroll lands a Payroll-less admin on Overview rather than a
//     page of 403s.
export function normaliseAdminNav(
  { parent, child }: UrlNav,
  isOwner: boolean,
  enabled: ReadonlySet<string> | null = null,
): UrlNav {
  const item = visibleAdminNav(isOwner, enabled).find((entry) => entry.id === parent);
  if (!item) return NAV_FALLBACK;

  const children = item.children ?? [];
  if (children.length === 0) return { parent: item.id, child: item.id };

  const match = children.find((c) => c.id === child);
  return { parent: item.id, child: match?.id ?? children[0].id };
}
