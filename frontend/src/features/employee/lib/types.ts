import type { LucideIcon } from "lucide-react";

export type EmployeeView =
  | "dashboard"
  | "claims"
  | "attendance"
  | "leave"
  | "payslips"
  | "settings";

export type EmployeeNavItem = {
  id: EmployeeView;
  label: string;
  icon: LucideIcon;
};
