import { LeaveView } from "./LeaveView";
import { LeaveApprovals } from "./LeaveApprovals";
import { TeamBalancesView } from "./TeamBalancesView";

// Leave tab: the sidebar sub-tab decides which view to show — "My leave"
// (LeaveView), the supervisor "Approvals" queue (LeaveApprovals), or "Team
// Balances" (TeamBalancesView).
export function LeavePage({ sub }: { sub: string }) {
  if (sub === "leave-approvals") return <LeaveApprovals />;
  if (sub === "leave-team") return <TeamBalancesView />;
  return <LeaveView />;
}
