import { apiGet, apiPost } from "@/shared/lib/api-client";

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

// A new shift on a project. The server owns the id and timestamps, and it is
// the server that clears any previous default when this one claims it.
export type CreateShift = {
  projectId: string;
  name: string;
  startTime: string;       // "HH:mm", 24h
  endTime: string;         // "HH:mm", 24h
  workingDays?: string | null;
  lunchBreakMinutes: number;
};

export const createShift = (body: CreateShift) => apiPost<Shift>("/shifts", body);

// Promote a shift to its project's default, clearing whichever held it before.
//
// Separate from create on purpose: the server decides the default on create
// (the first shift for a project gets it), so claiming it for a later one is
// its own deliberate act.
export const setDefaultShift = (id: string) => apiPost<Shift>(`/shifts/${id}/default`);
