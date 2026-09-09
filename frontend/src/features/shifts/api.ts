import { apiGet } from "@/shared/lib/api-client";

// A working pattern a project's employees are measured against: the hours the
// shift covers, which days it runs, and the unpaid break deducted from them.
//
// Shifts are per project, and one per project can be the default — that is the
// one an employee falls back to when nobody has assigned them a shift.
export type Shift = {
  id: string;
  projectId: string;
  name: string;
  startTime: string;      // "HH:mm", 24h
  endTime: string;        // "HH:mm", 24h
  workingDays: string | null;   // CSV ISO weekdays, "1,2,3,4,5"; null = Mon-Fri
  lunchBreakMinutes: number;
  isDefault: boolean;
};

export const getShifts = () => apiGet<Shift[]>("/shifts");
