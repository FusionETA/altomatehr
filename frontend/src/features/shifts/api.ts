import { apiDelete, apiGet, apiPost, apiPut } from "@/shared/lib/api-client";

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
  // Retired: no longer offered for new assignments and never a project's
  // default, but anyone already on it still resolves to it.
  isArchived: boolean;
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

// Edit an existing shift. No `projectId`: a shift's project is immutable after
// creation (see CreateShift), so the server does not read one here.
export type UpdateShift = {
  name: string;
  startTime: string;       // "HH:mm", 24h
  endTime: string;         // "HH:mm", 24h
  workingDays?: string | null;
  lunchBreakMinutes: number;
};

export const updateShift = (id: string, body: UpdateShift) =>
  apiPut<Shift>(`/shifts/${id}`, body);

// Permanently removes the shift — there is no archived state for shifts, so
// this is not reversible. The server refuses while anyone is still assigned to
// it, and the thrown error carries a message naming how many, so callers can
// show it as-is rather than inventing their own wording.
export const deleteShift = (id: string) => apiDelete<void>(`/shifts/${id}`);

// Retire a shift without destroying it — the reversible counterpart to
// deleteShift, and the right choice when a shift has history behind it.
// Archiving also clears the default flag (the server does that), and nothing is
// promoted in its place: until another shift is made default, the project falls
// back to the org working hours.
export const archiveShift = (id: string) => apiPost<Shift>(`/shifts/${id}/archive`);

// Bring an archived shift back. It does NOT regain the default it may have held
// before — that has to be claimed again deliberately.
export const restoreShift = (id: string) => apiPost<Shift>(`/shifts/${id}/restore`);
