import {
  CalendarClock,
  CalendarDays,
  ClipboardCheck,
  FileText,
  Home,
  Receipt,
} from "lucide-react";
import type { EmployeeNavItem } from "./types";

export const employeeNav: EmployeeNavItem[] = [
  { id: "dashboard", label: "Dashboard", icon: Home },
  { id: "claims", label: "Claims", icon: FileText },
  { id: "attendance", label: "Attendance", icon: CalendarClock },
  { id: "leave", label: "Leave", icon: CalendarDays },
  { id: "appraisals", label: "Appraisals", icon: ClipboardCheck },
  { id: "payslips", label: "Payslips", icon: Receipt },
];

export const mobilePrimaryNav = employeeNav.filter((item) =>
  ["dashboard", "claims", "attendance"].includes(item.id),
);

export const mobileMoreNav = employeeNav.filter((item) =>
  ["leave", "appraisals", "payslips"].includes(item.id),
);
