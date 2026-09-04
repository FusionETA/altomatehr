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
export type AdminChild = { id: string; label: string };

export type AdminNavItem = {
  id: string;
  label: string;
  icon: LucideIcon;
  built?: boolean;
  children?: AdminChild[];
};

export const adminNav: AdminNavItem[] = [
  { id: "overview", label: "Executive Overview", icon: LayoutDashboard, built: true },
  { id: "attendance", label: "Attendance", icon: CalendarClock },
  { id: "claims", label: "Claims", icon: Receipt, built: true },
  { id: "payroll", label: "Payroll", icon: Banknote },
  { id: "leave", label: "Leave", icon: CalendarDays },
  {
    id: "company",
    label: "Company/Employee",
    icon: Network,
    built: true,
    children: [
      { id: "company-structure", label: "Company Structure" },
      { id: "manage-employee", label: "Manage Employee" },
    ],
  },
  { id: "audit", label: "Activity Log", icon: History },
  {
    id: "settings",
    label: "System Settings",
    icon: Settings2,
    built: true,
    children: [
      { id: "settings-organization", label: "Organization" },
      { id: "settings-accounts", label: "Accounts" },
      { id: "settings-projects", label: "Projects" },
      { id: "settings-policies", label: "Policies" },
      { id: "settings-leave", label: "Leave Types" },
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
