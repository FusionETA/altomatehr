import { CalendarClock, CalendarDays, FileText, Home, Receipt } from "lucide-react";
import type { EmployeeNavItem, EmployeeView } from "./types";

// Mirrors the production employee-shell nav: each tab expands to its
// sub-pages, and supervisor-only children only show for supervisors.
export const employeeNav: EmployeeNavItem[] = [
  { id: "dashboard", label: "Dashboard", icon: Home },
  {
    id: "claims",
    label: "Claims",
    icon: FileText,
    children: [
      { id: "claims-mine", label: "My claims" },
      { id: "claims-queue", label: "Claims queue", supervisorOnly: true },
    ],
  },
  {
    id: "attendance",
    label: "Attendance",
    icon: CalendarClock,
    children: [
      { id: "att-dashboard", label: "Dashboard" },
      { id: "att-overtime", label: "Overtime" },
      { id: "att-approvals", label: "Approvals", supervisorOnly: true },
      { id: "att-history", label: "History" },
      { id: "att-team", label: "Team", supervisorOnly: true },
    ],
  },
  {
    id: "leave",
    label: "Leave",
    icon: CalendarDays,
    children: [
      { id: "leave-mine", label: "My Leave" },
      { id: "leave-approvals", label: "Approvals", supervisorOnly: true },
      { id: "leave-team", label: "Team Balances", supervisorOnly: true },
    ],
  },
  { id: "payslips", label: "Payslips", icon: Receipt },
];

export function findNavItem(id: EmployeeView): EmployeeNavItem {
  return employeeNav.find((item) => item.id === id) ?? employeeNav[0];
}

// The sub-page a tab opens to: its first child, or null when it has none.
export function defaultSubOf(item: EmployeeNavItem): string | null {
  return item.children?.[0]?.id ?? null;
}
