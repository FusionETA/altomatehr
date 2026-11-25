import type { Employee } from "../api";

// What someone is in this company, as Manage User shows it:
//   employee — on the staff only (Employee or Supervisor)
//   both     — Employee & admin: on the staff AND administers the company
//   admin    — admin only: administers, isn't on the staff (no payroll,
//              policy, leave or employee portal)
export type UserKind = "employee" | "both" | "admin";

export const isAdminRole = (role: string | null | undefined) => role === "Admin" || role === "Owner";

// Their staff role: their role for staff, the employment column for admins.
export const staffRole = (e: Pick<Employee, "role" | "employmentRole">): string | null =>
  isAdminRole(e.role) ? (e.employmentRole ?? null) : e.role;

export function userKind(e: Pick<Employee, "role" | "employmentRole">): UserKind {
  if (!isAdminRole(e.role)) return "employee";
  return staffRole(e) ? "both" : "admin";
}

export const USER_KIND_LABEL: Record<UserKind, string> = {
  employee: "Employee only",
  both: "Employee & admin",
  admin: "Admin only",
};
