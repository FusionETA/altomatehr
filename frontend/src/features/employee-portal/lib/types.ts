import type { LucideIcon } from "lucide-react";

export type EmployeeView =
  | "dashboard"
  | "claims"
  | "attendance"
  | "leave"
  | "payslips";

export type EmployeeSubItem = {
  id: string;
  label: string;
  supervisorOnly?: boolean;
};

export type EmployeeNavItem = {
  id: EmployeeView;
  label: string;
  icon: LucideIcon;
  children?: EmployeeSubItem[];
};
