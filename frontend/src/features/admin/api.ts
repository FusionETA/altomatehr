import { apiGet } from "@/shared/lib/api-client";

// The admin executive overview (GET /admin/overview). Cards are filled in one by one on
// the backend; unbuilt ones arrive as empty arrays and render their empty states.
export type ProjectClaimSpend = { project: string; totalAmount: number; claimCount: number };
export type AttendanceHealth = {
  project: string;
  total: number;
  onTime: number;
  late: number;
  missing: number;
  onLeave: number;
};
export type SlowOtApprover = {
  reviewerId: string;
  reviewerName: string;
  reviewedCount: number;
  pendingCount: number;
  averageHours: number;
};
export type StalePendingClaim = {
  id: string;
  claimNumber: string;
  title: string;
  employeeName: string;
  amount: number;
  daysPending: number;
  // Who the claim is waiting on. Empty means no approver is assigned to the
  // step it stalled at — a routing problem, not a slow reviewer.
  currentApprovers: string[];
};
export type UpcomingClaimRun = {
  cutoffDate: string;
  cutoffDay: number;
  daysUntilCutoff: number;
  claimsInRun: number;
  pendingInRun: number;
  totalAmountInRun: number;
};
export type OverturnedSupervisor = {
  supervisorId: string;
  supervisorName: string;
  overturnedCount: number;
  affectedEmployees: number;
  // The claims behind the count, so the card can drill into them.
  claimIds: string[];
};

// A foreign worker whose work permit has lapsed or lapses within 60 days.
export type WorkPermitAlert = {
  employeeId: string;
  employeeName: string;
  workPermitNumber: string | null;
  expiry: string;
  /** Negative once expired; 0 = expires today. */
  daysLeft: number;
};

export type AdminOverview = {
  enabledModules: string[];
  projectSpend: ProjectClaimSpend[];
  attendanceHealth: AttendanceHealth[];
  slowOtApprovers: SlowOtApprover[];
  stalePendingClaims: StalePendingClaim[];
  upcomingClaimRun: UpcomingClaimRun | null;
  overturnedSupervisors: { total: number; samples: OverturnedSupervisor[] };
  workPermitAlerts: WorkPermitAlert[];
};

export const getAdminOverview = () => apiGet<AdminOverview>("/admin/overview");
